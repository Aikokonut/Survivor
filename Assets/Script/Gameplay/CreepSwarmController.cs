using UnityEngine;

public class CreepSwarmController : MonoBehaviour
{
    [SerializeField] private Creep[] creeps;
    [SerializeField] private float deadZone = 0.1f;
    [SerializeField] private float groupMoveSpeed = 5.5f;
    [SerializeField] private float joystickSpeedPower = 1.05f;
    [SerializeField] private float formationRadius = 1.2f;
    [SerializeField] private float separationRadius = 0.75f;
    [SerializeField] private float separationWeight = 2.2f;
    [SerializeField] private float minFormationScale = 0.12f;
    [SerializeField] private float formationScaleSmooth = 3.5f;
    [SerializeField] private float corridorProbeDistance = 2.5f;
    [SerializeField] private float openWidthForFullFormation = 2.2f;
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groupTargetGroundRadius = 0.2f;

    private Vector2 _joystick;
    private Vector2 _groupTarget;
    private Vector2 _flowForward;
    private Vector2[] _positions;
    private Vector2[] _formationOffsets;
    private readonly RaycastHit2D[] _probeHits = new RaycastHit2D[1];
    private readonly Collider2D[] _groundHits = new Collider2D[1];
    private ContactFilter2D _wallFilter;
    private ContactFilter2D _groundFilter;
    private int _creepCount;
    private bool _groupTargetInitialized;
    private float _formationScale = 1f;
    private static int _debugTargetClampLogs;

    private void Awake()
    {
        _flowForward.x = 0f;
        _flowForward.y = 1f;
        _wallFilter = new ContactFilter2D();
        _wallFilter.useTriggers = false;
        _wallFilter.useLayerMask = true;
        _wallFilter.SetLayerMask(wallMask);
        _groundFilter = new ContactFilter2D();
        _groundFilter.useTriggers = true;
        _groundFilter.useLayerMask = true;
        _groundFilter.SetLayerMask(groundMask);
        groupMoveSpeed = 6.5f;
        joystickSpeedPower = 1f;
        formationScaleSmooth = 3.5f;
        openWidthForFullFormation = 2.2f;
        minFormationScale = 0.15f;
        CacheCreeps();
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
    }

    private void BuildFormationOffsets()
    {
        if (_creepCount <= 0)
        {
            return;
        }

        float goldenAngle = 2.399963229728653f;
        float count = _creepCount;
        for (int i = 0; i < _creepCount; i++)
        {
            float t = (i + 0.5f) / count;
            float radius = formationRadius * Mathf.Sqrt(t);
            float angle = i * goldenAngle;
            _formationOffsets[i].x = Mathf.Cos(angle) * radius;
            _formationOffsets[i].y = Mathf.Sin(angle) * radius;
        }
    }

    private void ConfigureCreeps()
    {
        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null)
            {
                continue;
            }

            int seed = i * 73856093;
            float speedScale = 0.82f + ((seed & 255) / 255f) * 0.36f;
            float accelScale = 0.75f + (((seed >> 8) & 255) / 255f) * 0.5f;
            creep.ConfigureSwarmMember(_formationOffsets[i], speedScale, accelScale);
        }
    }

    private void FixedUpdate()
    {
        if (_creepCount <= 0)
        {
            return;
        }

        float dt = Time.fixedDeltaTime;
        EnsureGroundMaskFromCreeps();
        EnsureGroupTarget();
        IntegrateGroupTarget(dt);
        CachePositions();
        UpdateFormationScale(dt);

        float sepRadius = Mathf.Lerp(separationRadius * 0.42f, separationRadius, _formationScale);
        float sepWeight = Mathf.Lerp(separationWeight * 0.35f, separationWeight, _formationScale);
        float moveIntent = _joystick.magnitude;

        // #region agent log
        if (Time.frameCount % 20 == 0)
        {
            int alive = 0;
            int falling = 0;
            for (int c = 0; c < _creepCount; c++)
            {
                Creep cr = creeps[c];
                if (cr == null || !cr.isActiveAndEnabled)
                {
                    continue;
                }
                alive++;
                if (cr.IsFalling())
                {
                    falling++;
                }
            }
            try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix6\",\"hypothesisId\":\"G\",\"location\":\"CreepSwarmController.cs:FixedUpdate\",\"message\":\"swarm state\",\"data\":{\"alive\":" + alive + ",\"falling\":" + falling + ",\"joyX\":" + _joystick.x.ToString("R") + ",\"joyY\":" + _joystick.y.ToString("R") + ",\"joyMag\":" + moveIntent.ToString("R") + ",\"formScale\":" + _formationScale.ToString("R") + ",\"tx\":" + _groupTarget.x.ToString("R") + ",\"ty\":" + _groupTarget.y.ToString("R") + ",\"groundMask\":" + groundMask.value + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
        }
        // #endregion

        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null || !creep.isActiveAndEnabled)
            {
                continue;
            }

            creep.TickSwarm(
                dt,
                _groupTarget,
                _flowForward,
                _formationScale,
                _positions,
                _creepCount,
                i,
                sepRadius,
                sepWeight,
                moveIntent);
        }
    }

    private void EnsureGroundMaskFromCreeps()
    {
        if (groundMask.value != 0 || creeps == null)
        {
            return;
        }

        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null)
            {
                continue;
            }

            LayerMask mask = creep.GetGroundMask();
            if (mask.value == 0)
            {
                continue;
            }

            groundMask = mask;
            _groundFilter.SetLayerMask(groundMask);
            // #region agent log
            try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix4\",\"hypothesisId\":\"G\",\"location\":\"CreepSwarmController.cs:EnsureGroundMaskFromCreeps\",\"message\":\"copied ground mask from creep\",\"data\":{\"groundMask\":" + groundMask.value + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
            // #endregion
            return;
        }
    }

    private void EnsureGroupTarget()
    {
        if (_groupTargetInitialized)
        {
            return;
        }

        float sumX = 0f;
        float sumY = 0f;
        int alive = 0;

        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null || !creep.isActiveAndEnabled)
            {
                continue;
            }

            Vector2 position = creep.GetPosition();
            sumX += position.x;
            sumY += position.y;
            alive++;
        }

        if (alive <= 0)
        {
            return;
        }

        float inv = 1f / alive;
        _groupTarget.x = sumX * inv;
        _groupTarget.y = sumY * inv;
        _groupTargetInitialized = true;
    }

    private void IntegrateGroupTarget(float dt)
    {
        float sqr = _joystick.sqrMagnitude;
        if (sqr <= 0f)
        {
            return;
        }

        float mag = Mathf.Sqrt(sqr);
        _flowForward.x = _joystick.x / mag;
        _flowForward.y = _joystick.y / mag;

        float curved = Mathf.Pow(mag, joystickSpeedPower);
        float step = groupMoveSpeed * curved * dt;
        Vector2 next;
        next.x = _groupTarget.x + _flowForward.x * step;
        next.y = _groupTarget.y + _flowForward.y * step;
        ApplyGroupTargetMove(next);
    }

    private void ApplyGroupTargetMove(Vector2 next)
    {
        if (groundMask.value == 0 || IsGroupTargetOnGround(next))
        {
            _groupTarget = next;
            return;
        }

        Vector2 slideX;
        slideX.x = next.x;
        slideX.y = _groupTarget.y;
        Vector2 slideY;
        slideY.x = _groupTarget.x;
        slideY.y = next.y;

        if (IsGroupTargetOnGround(slideX))
        {
            _groupTarget = slideX;
            // #region agent log
            LogTargetClamp("slideX", next);
            // #endregion
            return;
        }

        if (IsGroupTargetOnGround(slideY))
        {
            _groupTarget = slideY;
            // #region agent log
            LogTargetClamp("slideY", next);
            // #endregion
            return;
        }

        // #region agent log
        LogTargetClamp("blocked", next);
        // #endregion
    }

    private void LogTargetClamp(string mode, Vector2 next)
    {
        if (_debugTargetClampLogs >= 25)
        {
            return;
        }

        _debugTargetClampLogs++;
        try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix4\",\"hypothesisId\":\"G\",\"location\":\"CreepSwarmController.cs:ApplyGroupTargetMove\",\"message\":\"group target clamped\",\"data\":{\"mode\":\"" + mode + "\",\"nx\":" + next.x.ToString("R") + ",\"ny\":" + next.y.ToString("R") + ",\"tx\":" + _groupTarget.x.ToString("R") + ",\"ty\":" + _groupTarget.y.ToString("R") + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
    }

    private bool IsGroupTargetOnGround(Vector2 position)
    {
        if (groundMask.value == 0)
        {
            return true;
        }

        _groundFilter.SetLayerMask(groundMask);
        int hitCount = Physics2D.OverlapCircle(
            position,
            groupTargetGroundRadius,
            _groundFilter,
            _groundHits);
        return hitCount > 0;
    }

    private void UpdateFormationScale(float dt)
    {
        float targetScale = EvaluatePassageScale();
        float rate = formationScaleSmooth;
        if (targetScale > _formationScale)
        {
            rate *= 2.8f;
        }

        float step = rate * dt;
        _formationScale = Mathf.MoveTowards(_formationScale, targetScale, step);
    }

    private float EvaluatePassageScale()
    {
        float scale = 1f;

        if (groundMask.value != 0)
        {
            float widthHere = MeasureWalkableWidth(_groupTarget);
            Vector2 ahead;
            ahead.x = _groupTarget.x + _flowForward.x * 0.8f;
            ahead.y = _groupTarget.y + _flowForward.y * 0.8f;
            float widthAhead = MeasureWalkableWidth(ahead);
            float width = widthHere;
            if (widthAhead < width)
            {
                width = widthAhead;
            }

            if (width < openWidthForFullFormation && openWidthForFullFormation > 0.01f)
            {
                scale = width / openWidthForFullFormation;
            }
        }

        if (wallMask.value != 0)
        {
            _wallFilter.SetLayerMask(wallMask);
            float widthHere = MeasurePassageWidth(_groupTarget);
            Vector2 ahead;
            ahead.x = _groupTarget.x + _flowForward.x * 0.8f;
            ahead.y = _groupTarget.y + _flowForward.y * 0.8f;
            float widthAhead = MeasurePassageWidth(ahead);
            float width = widthHere;
            if (widthAhead < width)
            {
                width = widthAhead;
            }

            float wallScale = 1f;
            if (width < openWidthForFullFormation && openWidthForFullFormation > 0.01f)
            {
                wallScale = width / openWidthForFullFormation;
            }

            if (wallScale < scale)
            {
                scale = wallScale;
            }
        }

        if (scale < minFormationScale)
        {
            return minFormationScale;
        }

        if (scale > 1f)
        {
            return 1f;
        }

        return scale;
    }

    private float MeasureWalkableWidth(Vector2 origin)
    {
        Vector2 side;
        side.x = -_flowForward.y;
        side.y = _flowForward.x;

        float left = ProbeWalkableDistance(origin, side);
        float right = ProbeWalkableDistance(origin, -side);
        float width = left + right;
        if (width <= 0.01f)
        {
            return 0.01f;
        }

        return width;
    }

    private float ProbeWalkableDistance(Vector2 origin, Vector2 direction)
    {
        float step = 0.12f;
        float distance = 0f;
        for (float d = step; d <= corridorProbeDistance; d += step)
        {
            Vector2 sample;
            sample.x = origin.x + direction.x * d;
            sample.y = origin.y + direction.y * d;
            if (!IsGroupTargetOnGround(sample))
            {
                break;
            }

            distance = d;
        }

        return distance;
    }

    private float MeasurePassageWidth(Vector2 origin)
    {
        Vector2 side;
        side.x = -_flowForward.y;
        side.y = _flowForward.x;

        float left = ProbeDistance(origin, side);
        float right = ProbeDistance(origin, -side);
        float width = left + right;
        if (width <= 0.01f)
        {
            return 0.01f;
        }

        return width;
    }

    private float ProbeDistance(Vector2 origin, Vector2 direction)
    {
        int hits = Physics2D.Raycast(
            origin,
            direction,
            _wallFilter,
            _probeHits,
            corridorProbeDistance);
        if (hits <= 0)
        {
            return corridorProbeDistance;
        }

        return _probeHits[0].distance;
    }

    private void CachePositions()
    {
        for (int i = 0; i < _creepCount; i++)
        {
            Creep creep = creeps[i];
            if (creep == null || !creep.isActiveAndEnabled)
            {
                _positions[i].x = 0f;
                _positions[i].y = 0f;
                continue;
            }

            _positions[i] = creep.GetPosition();
        }
    }

    public void SetJoystickInput(Vector2 input)
    {
        if (input.sqrMagnitude < deadZone * deadZone)
        {
            _joystick.x = 0f;
            _joystick.y = 0f;
            return;
        }

        _joystick.x = input.x;
        _joystick.y = input.y;
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
