using UnityEngine;

public class CreepSwarmController : MonoBehaviour
{
    [SerializeField] private Creep[] creeps;
    [SerializeField] private float deadZone = 0.1f;
    [SerializeField] private float joystickSpeedPower = 1.05f;
    [SerializeField] private float groupLeadDistance = 0.85f;
    [SerializeField] private float formationRadius = 0.7f;
    [SerializeField] private float separationRadius = 0.38f;
    [SerializeField] private float separationWeight = 1.8f;
    [SerializeField] private float minSeparationRadius = 0.22f;
    [SerializeField] private float minFormationScale = 0.12f;
    [SerializeField] private float formationScaleSmooth = 12f;
    [SerializeField] private float corridorProbeDistance = 2.5f;
    [SerializeField] private float openWidthForFullFormation = 3.2f;
    [SerializeField] private float passageLookAhead = 2.4f;
    [SerializeField] private float probeInterval = 0.08f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groupTargetGroundRadius = 0.15f;
    [SerializeField] private bool evenSpacingMovement = true;

    private Vector2 _joystick;
    private Vector2 _groupTarget;
    private Vector2 _centroid;
    private Vector2 _flowForward = Vector2.up;
    private Vector2[] _positions;
    private Vector2[] _formationOffsets;
    private readonly RaycastHit2D[] _probeHits = new RaycastHit2D[1];
    private readonly Collider2D[] _groundHits = new Collider2D[1];
    private ContactFilter2D _wallFilter;
    private ContactFilter2D _groundFilter;
    private int _creepCount;
    private bool _groupTargetInitialized;
    private bool _hasGroundMask;
    private bool _hasWallMask;
    private float _formationScale = 1f;
    private float _targetFormationScale = 1f;
    private float _passageWidth = 99f;
    private float _probeTimer;

    private void Awake()
    {
        _hasGroundMask = groundMask.value != 0;
        _hasWallMask = wallMask.value != 0;

        _wallFilter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
        _wallFilter.SetLayerMask(wallMask);

        _groundFilter = new ContactFilter2D { useTriggers = true, useLayerMask = true };
        _groundFilter.SetLayerMask(groundMask);

        CacheCreeps();
    }

    private void Start()
    {
        ConfigureCreeps();
    }

    private void CacheCreeps()
    {
        if (creeps == null)
        {
            _creepCount = 0;
            _positions = null;
            _formationOffsets = null;
            return;
        }

        _creepCount = creeps.Length;
        _positions = new Vector2[_creepCount];
        _formationOffsets = new Vector2[_creepCount];
        BuildFormationOffsets();
        ConfigureCreeps();
        _groupTargetInitialized = false;
        _formationScale = 1f;
        _targetFormationScale = 1f;
    }

    private void BuildFormationOffsets()
    {
        if (_creepCount <= 0) return;
        const float goldenAngle = 2.39996323f;
        float count = _creepCount;
        float effectiveRadius = evenSpacingMovement
            ? Mathf.Max(formationRadius, Mathf.Sqrt(count) * 0.22f)
            : formationRadius;
        for (int i = 0; i < _creepCount; i++)
        {
            float radius = effectiveRadius * Mathf.Sqrt((i + 0.5f) / count);
            float angle = i * goldenAngle;
            _formationOffsets[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }

    private void ConfigureCreeps()
    {
        for (int i = 0; i < _creepCount; i++)
        {
            if (creeps[i] == null) continue;
            int seed = i * 73856093;
            float speedScale = 0.82f + ((seed & 255) / 255f) * 0.36f;
            float accelScale = 0.75f + (((seed >> 8) & 255) / 255f) * 0.5f;
            creeps[i].ConfigureSwarmMember(_formationOffsets[i], speedScale, accelScale);
        }
    }

    private void FixedUpdate()
    {
        if (_creepCount <= 0) return;

        float dt = Time.fixedDeltaTime;
        EnsureGroundMaskFromCreeps();
        UpdateCentroidAndPositions();
        EnsureGroupTarget();
        UpdateGroupTargetFromCentroid();
        UpdateFormationScale(dt);

        float sepRadius = evenSpacingMovement
            ? Mathf.Clamp(separationRadius, 0.48f, 0.56f)
            : Mathf.Max(minSeparationRadius, separationRadius * Mathf.Lerp(0.7f, 1f, _formationScale));
        float currentScale = evenSpacingMovement ? 1f : _formationScale;
        float moveIntent = _joystick.magnitude;

        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep != null && creep.isActiveAndEnabled)
            {
                creep.TickSwarm(dt, _groupTarget, _flowForward, _joystick, currentScale, _positions, _creepCount, i, sepRadius, separationWeight, moveIntent, evenSpacingMovement);
            }
        }
    }

    private void EnsureGroundMaskFromCreeps()
    {
        if (_hasGroundMask || creeps == null) return;
        for (int i = 0; i < _creepCount; i++)
        {
            if (creeps[i] == null) continue;
            LayerMask mask = creeps[i].GetGroundMask();
            if (mask.value == 0) continue;
            groundMask = mask;
            _groundFilter.SetLayerMask(groundMask);
            _hasGroundMask = true;
            return;
        }
    }

    private void UpdateCentroidAndPositions()
    {
        Vector2 sum = Vector2.zero;
        int alive = 0;
        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null || !creep.isActiveAndEnabled || creep.IsFalling())
            {
                _positions[i] = new Vector2(9999f, 9999f);
                continue;
            }
            Vector2 pos = creep.GetPosition();
            _positions[i] = pos;
            sum += pos;
            alive++;
        }
        if (alive > 0) _centroid = sum / alive;
    }

    private void EnsureGroupTarget()
    {
        if (_groupTargetInitialized) return;
        _groupTarget = _centroid;
        _groupTargetInitialized = true;
    }

    private void UpdateGroupTargetFromCentroid()
    {
        float sqr = _joystick.sqrMagnitude;
        if (sqr <= 0.0001f)
        {
            _groupTarget = _centroid;
            CenterOnPassage(ref _groupTarget);
            return;
        }

        float mag = Mathf.Sqrt(sqr);
        _flowForward = _joystick / mag;

        float lead = Mathf.Max(groupLeadDistance * 0.35f, groupLeadDistance * Mathf.Pow(mag, joystickSpeedPower));
        Vector2 next = _centroid + _flowForward * lead;
        if (!IsGroupTargetOnGround(next))
        {
            next = _centroid + _flowForward * (lead * 0.45f);
        }

        ApplyGroupTargetMove(next);
        CenterOnPassage(ref _groupTarget);

        float maxLead = groupLeadDistance * 1.35f;
        if (Vector2.SqrMagnitude(_groupTarget - _centroid) > maxLead * maxLead || !IsGroupTargetOnGround(_groupTarget))
        {
            _groupTarget = _centroid;
            CenterOnPassage(ref _groupTarget);
        }
    }

    private void CenterOnPassage(ref Vector2 target)
    {
        if (!_hasGroundMask && !_hasWallMask) return;

        Vector2 side = new Vector2(-_flowForward.y, _flowForward.x);
        float left = _hasGroundMask ? ProbeWalkableDistance(target, -side) : ProbeDistance(target, -side);
        float right = _hasGroundMask ? ProbeWalkableDistance(target, side) : ProbeDistance(target, side);

        _passageWidth = left + right;
        float shift = (right - left) * 0.5f;
        if (Mathf.Abs(shift) <= 0.001f) return;

        Vector2 centered = target + side * shift;
        if (!_hasGroundMask || IsGroupTargetOnGround(centered)) target = centered;
    }

    private void ApplyGroupTargetMove(Vector2 next)
    {
        if (!_hasGroundMask || IsGroupTargetOnGround(next)) { _groupTarget = next; return; }
        Vector2 slideX = new Vector2(next.x, _groupTarget.y);
        if (IsGroupTargetOnGround(slideX)) { _groupTarget = slideX; return; }
        Vector2 slideY = new Vector2(_groupTarget.x, next.y);
        if (IsGroupTargetOnGround(slideY)) { _groupTarget = slideY; }
    }

    private bool IsGroupTargetOnGround(Vector2 position)
    {
        return !_hasGroundMask || Physics2D.OverlapCircle(position, groupTargetGroundRadius, _groundFilter, _groundHits) > 0;
    }

    private void UpdateFormationScale(float dt)
    {
        _probeTimer -= dt;
        if (_probeTimer <= 0f)
        {
            _probeTimer = probeInterval;
            _targetFormationScale = EvaluatePassageScale();
        }
        _formationScale = Mathf.MoveTowards(_formationScale, _targetFormationScale, formationScaleSmooth * dt);
    }

    private float EvaluatePassageScale()
    {
        if (!_hasGroundMask && !_hasWallMask) return 1f;

        float width = Mathf.Min(MeasureNarrowestAhead(_centroid), MeasureNarrowestAhead(_groupTarget));
        _passageWidth = Mathf.Min(_passageWidth, width);

        float scale = (openWidthForFullFormation > 0.01f && width < openWidthForFullFormation)
            ? width / openWidthForFullFormation
            : 1f;
        return Mathf.Clamp(scale, minFormationScale, 1f);
    }

    private float MeasureNarrowestAhead(Vector2 origin)
    {
        float minWidth = 999f;
        for (int i = 0; i <= 2; i++)
        {
            Vector2 sample = origin + _flowForward * (passageLookAhead * (i * 0.5f));
            if (_hasGroundMask) minWidth = Mathf.Min(minWidth, MeasureWalkableWidth(sample));
            if (_hasWallMask) minWidth = Mathf.Min(minWidth, MeasurePassageWidth(sample));
        }
        return minWidth > 900f ? openWidthForFullFormation : minWidth;
    }

    private float MeasureWalkableWidth(Vector2 origin)
    {
        Vector2 side = new Vector2(-_flowForward.y, _flowForward.x);
        return Mathf.Max(ProbeWalkableDistance(origin, side) + ProbeWalkableDistance(origin, -side), 0.01f);
    }

    private float ProbeWalkableDistance(Vector2 origin, Vector2 direction)
    {
        float distance = 0f;
        for (float d = 0.25f; d <= corridorProbeDistance; d += 0.25f)
        {
            if (!IsGroupTargetOnGround(origin + direction * d)) break;
            distance = d;
        }
        return distance;
    }

    private float MeasurePassageWidth(Vector2 origin)
    {
        Vector2 side = new Vector2(-_flowForward.y, _flowForward.x);
        return Mathf.Max(ProbeDistance(origin, side) + ProbeDistance(origin, -side), 0.01f);
    }

    private float ProbeDistance(Vector2 origin, Vector2 direction)
    {
        return Physics2D.Raycast(origin, direction, _wallFilter, _probeHits, corridorProbeDistance) <= 0
            ? corridorProbeDistance
            : _probeHits[0].distance;
    }

    public void SetJoystickInput(Vector2 input)
    {
        _joystick = input.sqrMagnitude < deadZone * deadZone ? Vector2.zero : input;
    }

    public void SetCreeps(Creep[] newCreeps)
    {
        creeps = newCreeps;
        CacheCreeps();
    }

    public int GetCreepCount()
    {
        return _creepCount;
    }
}
