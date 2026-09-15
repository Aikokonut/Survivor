using UnityEngine;

public class CreepSwarmController : MonoBehaviour
{
    [SerializeField] private Creep[] creeps;
    [SerializeField] private float deadZone = 0.1f;
    [SerializeField] private float formationRadius = 0.7f;
    [SerializeField] private float separationRadius = 0.48f;
    [SerializeField] private float separationWeight = 1.8f;
    [SerializeField] private float minFormationScale = 0.12f;
    [SerializeField] private float formationScaleSmooth = 12f;
    [SerializeField] private float corridorProbeDistance = 2.5f;
    [SerializeField] private float openWidthForFullFormation = 3.2f;
    [SerializeField] private float passageLookAhead = 2.4f;
    [SerializeField] private float probeInterval = 0.08f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groupTargetGroundRadius = 0.15f;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float cameraFollowSpeed = 5f;
    [SerializeField] private float cameraTurnFollowScale = 0.55f;
    [SerializeField] private float cameraRetargetDistance = 2.5f;
    [SerializeField] private float cameraCatchUpSpeed = 8f;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 0f, -10f);
    [SerializeField] private float packFollowDistance = 0.85f;

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
    private float _probeTimer;
    private Creep _leaderCreep;
    private System.Action<Creep> _onLeaderDiedCallback;
    private bool _cameraInitialized;
    private Vector2 _cameraLookAt;
    private Vector2 _anchorPosition;
    private bool _isAnchored;
    private int _leaderIndex = -1;
    private Vector2 _cameraFlow = Vector2.up;

    private void Awake()
    {
        _onLeaderDiedCallback = OnLeaderDied;
        if (cameraTransform == null && targetCamera != null) cameraTransform = targetCamera.transform;

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
        EnsureLeader();
    }

    private void OnEnable()
    {
        if (_onLeaderDiedCallback == null) _onLeaderDiedCallback = OnLeaderDied;
        if (_leaderCreep != null)
        {
            _leaderCreep.onDied -= _onLeaderDiedCallback;
            _leaderCreep.onDied += _onLeaderDiedCallback;
        }
    }

    private void OnDisable()
    {
        if (_leaderCreep != null && _onLeaderDiedCallback != null) _leaderCreep.onDied -= _onLeaderDiedCallback;
    }

    private void CacheCreeps()
    {
        if (creeps == null)
        {
            _creepCount = 0;
            _positions = null;
            _formationOffsets = null;
            SetLeader(null);
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
        EnsureLeader();
    }

    private void BuildFormationOffsets()
    {
        if (_creepCount <= 0) return;
        const float goldenAngle = 2.39996323f;
        float count = _creepCount;
        float radiusMax = Mathf.Max(formationRadius, Mathf.Sqrt(count) * 0.22f);
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < _creepCount; i++)
        {
            float radius = radiusMax * Mathf.Sqrt((i + 0.5f) / count);
            float angle = i * goldenAngle;
            _formationOffsets[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            sum += _formationOffsets[i];
        }
        Vector2 mean = sum / count;
        for (int i = 0; i < _creepCount; i++) _formationOffsets[i] -= mean;
    }

    private void ConfigureCreeps()
    {
        for (int i = 0; i < _creepCount; i++)
        {
            if (creeps[i] != null) creeps[i].ConfigureSwarmMember(_formationOffsets[i]);
        }
    }

    private void FixedUpdate()
    {
        if (_creepCount <= 0) return;

        float dt = Time.fixedDeltaTime;
        EnsureGroundMaskFromCreeps();
        UpdatePositions();
        EnsureLeader();
        EnsureGroupTarget();
        UpdateGroupTarget();
        UpdateFormationScale(dt);

        float scale = _joystick.sqrMagnitude <= 0.0001f ? 1f : _formationScale;
        float moveIntent = _joystick.magnitude;
        Vector2 leaderPos = _leaderCreep != null ? _leaderCreep.GetPosition() : _groupTarget;

        if (_leaderCreep != null && _leaderCreep.isActiveAndEnabled && _leaderIndex >= 0)
        {
            TickOne(_leaderCreep, _leaderIndex, scale, moveIntent, leaderPos, dt);
            leaderPos = _leaderCreep.GetPosition();
            _positions[_leaderIndex] = leaderPos;
            if (moveIntent > 0.05f)
            {
                _groupTarget = leaderPos - _flowForward * packFollowDistance;
                if (!IsOnGround(_groupTarget)) _groupTarget = leaderPos;
                _anchorPosition = leaderPos;
            }
        }

        for (int i = 0; i < _creepCount; i++)
        {
            if (i == _leaderIndex || creeps[i] == null || !creeps[i].isActiveAndEnabled) continue;
            TickOne(creeps[i], i, scale, moveIntent, leaderPos, dt);
        }

        UpdateCameraFollow(dt);
    }

    private void TickOne(Creep creep, int index, float scale, float moveIntent, Vector2 leaderPos, float dt)
    {
        creep.TickSwarm(dt, _groupTarget, _flowForward, _joystick, scale, _positions, _creepCount, index, separationRadius, separationWeight, moveIntent, leaderPos, _leaderIndex);
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

    private void UpdatePositions()
    {
        Vector2 sum = Vector2.zero;
        int alive = 0;
        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null || !creep.isActiveAndEnabled || creep.IsDead())
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
        Vector2 start = _leaderCreep != null ? _leaderCreep.GetPosition() : _centroid;
        _groupTarget = start;
        _anchorPosition = start;
        _isAnchored = true;
        _groupTargetInitialized = true;
    }

    private void UpdateGroupTarget()
    {
        Vector2 leaderPos = _leaderCreep != null ? _leaderCreep.GetPosition() : _groupTarget;

        if (_joystick.sqrMagnitude <= 0.0001f)
        {
            if (!_isAnchored)
            {
                _isAnchored = true;
                _anchorPosition = leaderPos - _flowForward * packFollowDistance;
                if (!IsOnGround(_anchorPosition)) _anchorPosition = leaderPos;
            }
            _groupTarget = _anchorPosition;
            return;
        }

        _isAnchored = false;
        _flowForward = _joystick.normalized;
        if (_leaderCreep == null) return;

        _groupTarget = leaderPos - _flowForward * packFollowDistance;
        if (!IsOnGround(_groupTarget)) _groupTarget = leaderPos;
        CenterOnPassage(ref _groupTarget);
        _anchorPosition = leaderPos;
    }

    private void CenterOnPassage(ref Vector2 target)
    {
        if (!_hasGroundMask && !_hasWallMask) return;
        Vector2 side = new Vector2(-_flowForward.y, _flowForward.x);
        float left = _hasGroundMask ? ProbeWalkable(target, -side) : ProbeWall(target, -side);
        float right = _hasGroundMask ? ProbeWalkable(target, side) : ProbeWall(target, side);
        float shift = (right - left) * 0.5f;
        if (Mathf.Abs(shift) <= 0.001f) return;
        Vector2 centered = target + side * shift;
        if (!_hasGroundMask || IsOnGround(centered)) target = centered;
    }

    private bool IsOnGround(Vector2 position)
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
        float width = Mathf.Min(MeasureAhead(_centroid), MeasureAhead(_groupTarget));
        float scale = width < openWidthForFullFormation ? width / openWidthForFullFormation : 1f;
        return Mathf.Clamp(scale, minFormationScale, 1f);
    }

    private float MeasureAhead(Vector2 origin)
    {
        float minWidth = 999f;
        for (int i = 0; i <= 2; i++)
        {
            Vector2 sample = origin + _flowForward * (passageLookAhead * (i * 0.5f));
            Vector2 side = new Vector2(-_flowForward.y, _flowForward.x);
            if (_hasGroundMask) minWidth = Mathf.Min(minWidth, ProbeWalkable(sample, side) + ProbeWalkable(sample, -side));
            if (_hasWallMask) minWidth = Mathf.Min(minWidth, ProbeWall(sample, side) + ProbeWall(sample, -side));
        }
        return minWidth > 900f ? openWidthForFullFormation : Mathf.Max(minWidth, 0.01f);
    }

    private float ProbeWalkable(Vector2 origin, Vector2 direction)
    {
        float distance = 0f;
        for (float d = 0.25f; d <= corridorProbeDistance; d += 0.25f)
        {
            if (!IsOnGround(origin + direction * d)) break;
            distance = d;
        }
        return distance;
    }

    private float ProbeWall(Vector2 origin, Vector2 direction)
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

    public int GetCreepCount() { return _creepCount; }
    public Creep GetLeader() { return _leaderCreep; }

    public void SetCamera(Transform camTransform)
    {
        cameraTransform = camTransform;
        _cameraInitialized = false;
    }

    private void EnsureLeader()
    {
        if (_leaderCreep != null && _leaderCreep.isActiveAndEnabled && !_leaderCreep.IsDead()) return;
        Vector2 searchPos = _leaderCreep != null ? _leaderCreep.GetPosition() : _centroid;
        SetLeader(FindNearestCreep(searchPos));
    }

    private void OnLeaderDied(Creep deadCreep)
    {
        Vector2 deathPos = deadCreep != null ? deadCreep.GetPosition() : _centroid;
        SetLeader(null);
        SetLeader(FindNearestCreep(deathPos));
    }

    private void SetLeader(Creep newLeader)
    {
        if (_leaderCreep == newLeader) return;

        if (_leaderCreep != null)
        {
            _leaderCreep.onDied -= _onLeaderDiedCallback;
            _leaderCreep.SetLeader(false);
        }

        _leaderCreep = newLeader;
        _leaderIndex = -1;

        if (_leaderCreep == null) return;

        _leaderCreep.onDied += _onLeaderDiedCallback;
        _leaderCreep.SetLeader(true);
        for (int i = 0; i < _creepCount; i++)
        {
            if (creeps[i] != _leaderCreep) continue;
            _leaderIndex = i;
            break;
        }
    }

    private Creep FindNearestCreep(Vector2 position)
    {
        Creep nearest = null;
        float minSqr = float.MaxValue;
        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null || !creep.isActiveAndEnabled || creep.IsDead()) continue;
            float sqr = (creep.GetPosition() - position).sqrMagnitude;
            if (sqr >= minSqr) continue;
            minSqr = sqr;
            nearest = creep;
        }
        return nearest;
    }

    private void UpdateCameraFollow(float dt)
    {
        if (cameraTransform == null || _leaderCreep == null) return;

        Vector2 leaderPos = _leaderCreep.GetPosition();
        Vector3 current = cameraTransform.position;
        float z = cameraOffset.z != 0f ? cameraOffset.z : current.z;

        if (!_cameraInitialized || cameraFollowSpeed <= 0f)
        {
            _cameraLookAt = leaderPos;
            cameraTransform.position = new Vector3(leaderPos.x + cameraOffset.x, leaderPos.y + cameraOffset.y, z);
            _cameraInitialized = true;
            _cameraFlow = _flowForward;
            return;
        }

        float distSqr = (leaderPos - _cameraLookAt).sqrMagnitude;
        float retargetSqr = cameraRetargetDistance * cameraRetargetDistance;
        bool moving = _joystick.sqrMagnitude > 0.0001f;
        bool far = distSqr > retargetSqr;

        if (moving || far)
        {
            float lookSpeed = far ? cameraCatchUpSpeed : cameraFollowSpeed;
            if (moving)
            {
                if (Vector2.Dot(_cameraFlow, _flowForward) < 0.92f) lookSpeed *= cameraTurnFollowScale;
                _cameraFlow = Vector2.Lerp(_cameraFlow, _flowForward, Mathf.Clamp01(8f * dt)).normalized;
            }
            _cameraLookAt = Vector2.Lerp(_cameraLookAt, leaderPos, Mathf.Clamp01(lookSpeed * dt));
        }

        Vector3 target = new Vector3(_cameraLookAt.x + cameraOffset.x, _cameraLookAt.y + cameraOffset.y, z);
        cameraTransform.position = Vector3.Lerp(current, target, Mathf.Clamp01(cameraFollowSpeed * dt));
    }
}
