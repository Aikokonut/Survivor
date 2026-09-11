using UnityEngine;

public class Creep : MonoBehaviour
{
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private float maxSpeed = 4.5f;
    [SerializeField] private float acceleration = 16f;
    [SerializeField] private float deceleration = 11f;
    [SerializeField] private float arriveRadius = 0.9f;
    [SerializeField] private float slotFollowWeight = 1f;
    [SerializeField] private float queueSpacing = 0.55f;
    [SerializeField] private float yieldDistance = 0.7f;
    [SerializeField] private float yieldSpeedScale = 0.28f;
    [SerializeField] private float yieldLaneWidth = 0.55f;
    [SerializeField] private float obstacleCheckDistance = 0.55f;
    [SerializeField] private float obstacleAvoidWeight = 1.4f;
    [SerializeField] private float groundCheckRadius = 0.18f;
    [SerializeField] private float fallGraceTime = 0.12f;
    [SerializeField] private float carefulLeaveSpeed = 1.8f;
    [SerializeField] private float walkBobAmplitude = 0.12f;
    [SerializeField] private float walkBobSpeed = 22f;
    [SerializeField] private float walkBobAngle = 10f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask abyssMask;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float fallScaleSpeed = 2.5f;
    [SerializeField] private float minFallScale = 0.05f;

    private Transform _transform;
    private Vector2 _formationOffset;
    private Vector2 _velocity;
    private Vector2 _desired;
    private Vector2 _seek;
    private Vector2 _separation;
    private Vector2 _nextPosition;
    private Vector3 _baseScale;
    private Vector3 _fallScale;
    private readonly Collider2D[] _groundHits = new Collider2D[1];
    private readonly RaycastHit2D[] _obstacleHits = new RaycastHit2D[1];
    private ContactFilter2D _groundFilter;
    private ContactFilter2D _obstacleFilter;
    private float _speedScale;
    private float _accelScale;
    private float _airTime;
    private float _bobPhase;
    private float _moveIntent;
    private float _bobLogTimer;
    private bool _isGrounded;
    private bool _isFalling;
    private bool _configured;
    private static int _debugFallLogs;

    private void Awake()
    {
        CacheReferences();
    }

    private void CacheReferences()
    {
        _transform = transform;
        _baseScale = _transform.localScale;
        _fallScale = _baseScale;
        _formationOffset = Vector2.zero;
        _velocity = Vector2.zero;
        _desired = Vector2.zero;
        _seek = Vector2.zero;
        _separation = Vector2.zero;
        _nextPosition = Vector2.zero;
        _speedScale = 1f;
        _accelScale = 1f;
        _airTime = 0f;
        _bobPhase = Mathf.Abs(GetInstanceID() % 628) * 0.01f;
        _bobLogTimer = 0f;
        _moveIntent = 0f;
        walkBobAmplitude = 0.12f;
        walkBobSpeed = 22f;
        walkBobAngle = 10f;
        _isGrounded = false;
        _isFalling = false;
        _configured = false;

        _groundFilter = new ContactFilter2D();
        _groundFilter.useTriggers = true;
        _groundFilter.useLayerMask = true;
        _groundFilter.SetLayerMask(groundMask);

        _obstacleFilter = new ContactFilter2D();
        _obstacleFilter.useTriggers = false;
        _obstacleFilter.useLayerMask = true;
        _obstacleFilter.SetLayerMask(obstacleMask);

        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.simulated = true;
            body.gravityScale = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    public void ConfigureSwarmMember(Vector2 formationOffset, float speedScale, float accelScale)
    {
        _formationOffset = formationOffset;
        _speedScale = speedScale;
        _accelScale = accelScale;
        _configured = true;
    }

    public Vector2 GetPosition()
    {
        return _transform.position;
    }

    public LayerMask GetGroundMask()
    {
        return groundMask;
    }

    public bool IsFalling()
    {
        return _isFalling;
    }

    public void TickSwarm(
        float dt,
        Vector2 groupTarget,
        Vector2 flowForward,
        float formationScale,
        Vector2[] positions,
        int count,
        int selfIndex,
        float separationRadius,
        float separationWeight,
        float moveIntent)
    {
        if (_isFalling)
        {
            TickFallScale(dt);
            return;
        }

        if (!_configured)
        {
            return;
        }

        _moveIntent = moveIntent;
        UpdateGroundedState();
        IntegrateMotion(
            dt,
            groupTarget,
            flowForward,
            formationScale,
            positions,
            count,
            selfIndex,
            separationRadius,
            separationWeight);
        UpdateFallState(dt);
        ApplyWalkBob();
    }

    private void UpdateGroundedState()
    {
        if (groundMask.value == 0)
        {
            _isGrounded = true;
            _airTime = 0f;
            return;
        }

        _isGrounded = IsOnGround(_transform.position);
        if (_isGrounded)
        {
            _airTime = 0f;
        }
    }

    private bool IsOnGround(Vector2 position)
    {
        if (groundMask.value == 0)
        {
            return true;
        }

        _groundFilter.SetLayerMask(groundMask);
        int hitCount = Physics2D.OverlapCircle(
            position,
            groundCheckRadius,
            _groundFilter,
            _groundHits);
        return hitCount > 0;
    }

    private void UpdateFallState(float dt)
    {
        if (groundMask.value == 0)
        {
            return;
        }

        if (_isGrounded)
        {
            _airTime = 0f;
            return;
        }

        _airTime += dt;
        if (_airTime < fallGraceTime)
        {
            return;
        }

        // #region agent log
        if (_debugFallLogs < 30)
        {
            _debugFallLogs++;
            Vector2 p = _transform.position;
            try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix4\",\"hypothesisId\":\"A\",\"location\":\"Creep.cs:UpdateFallState\",\"message\":\"grace leave-ground fall\",\"data\":{\"name\":\"" + name + "\",\"px\":" + p.x.ToString("R") + ",\"py\":" + p.y.ToString("R") + ",\"vx\":" + _velocity.x.ToString("R") + ",\"vy\":" + _velocity.y.ToString("R") + ",\"spd\":" + _velocity.magnitude.ToString("R") + ",\"airTime\":" + _airTime.ToString("R") + ",\"moveIntent\":" + _moveIntent.ToString("R") + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
        }
        // #endregion
        BeginFall();
    }

    private void IntegrateMotion(
        float dt,
        Vector2 groupTarget,
        Vector2 flowForward,
        float formationScale,
        Vector2[] positions,
        int count,
        int selfIndex,
        float separationRadius,
        float separationWeight)
    {
        Vector2 position = _transform.position;
        BuildDesiredVelocity(
            position,
            groupTarget,
            flowForward,
            formationScale,
            positions,
            count,
            selfIndex,
            separationRadius,
            separationWeight);
        ApplyObstacleAvoidance(position);
        AccelerateTowardDesired(dt);
        ApplyMovement(position, dt);
    }

    private void BuildDesiredVelocity(
        Vector2 position,
        Vector2 groupTarget,
        Vector2 flowForward,
        float formationScale,
        Vector2[] positions,
        int count,
        int selfIndex,
        float separationRadius,
        float separationWeight)
    {
        float personalMax = maxSpeed * _speedScale;
        float compress = 1f - formationScale;
        if (compress < 0f)
        {
            compress = 0f;
        }

        float ox = _formationOffset.x * formationScale;
        float oy = _formationOffset.y * formationScale;
        ResolveSlotOnGround(groupTarget, ref ox, ref oy);

        float slotX = groupTarget.x + ox;
        float slotY = groupTarget.y + oy;

        if (formationScale < 0.45f)
        {
            float queueRank = EstimateQueueRank(position, groupTarget, flowForward, positions, count, selfIndex);
            float queuePull = (1f - formationScale / 0.45f);
            slotX -= flowForward.x * queueRank * queueSpacing * queuePull;
            slotY -= flowForward.y * queueRank * queueSpacing * queuePull;
        }

        float toSlotX = slotX - position.x;
        float toSlotY = slotY - position.y;
        float seekSqr = toSlotX * toSlotX + toSlotY * toSlotY;

        _seek.x = 0f;
        _seek.y = 0f;
        _separation.x = 0f;
        _separation.y = 0f;

        if (seekSqr > 0.0001f)
        {
            float dist = Mathf.Sqrt(seekSqr);
            float speed = personalMax;
            float arrive = Mathf.Lerp(0.25f, arriveRadius * 0.75f, formationScale);
            if (dist < arrive)
            {
                speed *= dist / arrive;
            }

            float inv = (speed / dist) * slotFollowWeight;
            _seek.x = toSlotX * inv;
            _seek.y = toSlotY * inv;
        }

        ApplySeparation(position, positions, count, selfIndex, separationRadius, separationWeight, personalMax);
        if (formationScale < 0.45f)
        {
            ApplyYield(position, groupTarget, flowForward, positions, count, selfIndex);
        }

        _desired.x = _seek.x + _separation.x;
        _desired.y = _seek.y + _separation.y;
    }

    private void ResolveSlotOnGround(Vector2 groupTarget, ref float ox, ref float oy)
    {
        if (groundMask.value == 0)
        {
            return;
        }

        Vector2 slot;
        slot.x = groupTarget.x + ox;
        slot.y = groupTarget.y + oy;
        if (IsOnGround(slot))
        {
            return;
        }

        for (int i = 0; i < 5; i++)
        {
            ox *= 0.55f;
            oy *= 0.55f;
            slot.x = groupTarget.x + ox;
            slot.y = groupTarget.y + oy;
            if (IsOnGround(slot))
            {
                return;
            }
        }

        ox = 0f;
        oy = 0f;
    }

    private float EstimateQueueRank(
        Vector2 position,
        Vector2 groupTarget,
        Vector2 flowForward,
        Vector2[] positions,
        int count,
        int selfIndex)
    {
        if (positions == null || count <= 0)
        {
            return 0f;
        }

        float myAlong = (groupTarget.x - position.x) * flowForward.x + (groupTarget.y - position.y) * flowForward.y;
        float rank = 0f;

        for (int i = 0; i < count; i++)
        {
            if (i == selfIndex)
            {
                continue;
            }

            float along = (groupTarget.x - positions[i].x) * flowForward.x
                + (groupTarget.y - positions[i].y) * flowForward.y;
            if (along < myAlong)
            {
                rank += 1f;
            }
        }

        return rank;
    }

    private void ApplySeparation(
        Vector2 position,
        Vector2[] positions,
        int count,
        int selfIndex,
        float separationRadius,
        float separationWeight,
        float personalMax)
    {
        if (positions == null || count <= 0)
        {
            return;
        }

        float sepSqr = separationRadius * separationRadius;
        float pushX = 0f;
        float pushY = 0f;

        for (int i = 0; i < count; i++)
        {
            if (i == selfIndex)
            {
                continue;
            }

            float dx = position.x - positions[i].x;
            float dy = position.y - positions[i].y;
            float sqr = dx * dx + dy * dy;
            if (sqr <= 0.0001f || sqr >= sepSqr)
            {
                continue;
            }

            float dist = Mathf.Sqrt(sqr);
            float strength = (separationRadius - dist) / separationRadius;
            float inv = (strength * separationWeight * personalMax) / dist;
            pushX += dx * inv;
            pushY += dy * inv;
        }

        _separation.x = pushX;
        _separation.y = pushY;
    }

    private void ApplyYield(
        Vector2 position,
        Vector2 groupTarget,
        Vector2 flowForward,
        Vector2[] positions,
        int count,
        int selfIndex)
    {
        if (positions == null || count <= 0)
        {
            return;
        }

        float sideX = -flowForward.y;
        float sideY = flowForward.x;
        float myAlong = (groupTarget.x - position.x) * flowForward.x + (groupTarget.y - position.y) * flowForward.y;
        float myLane = (position.x - groupTarget.x) * sideX + (position.y - groupTarget.y) * sideY;
        float yieldSqr = yieldDistance * yieldDistance;
        bool mustYield = false;

        for (int i = 0; i < count; i++)
        {
            if (i == selfIndex)
            {
                continue;
            }

            float dx = position.x - positions[i].x;
            float dy = position.y - positions[i].y;
            float sqr = dx * dx + dy * dy;
            if (sqr > yieldSqr)
            {
                continue;
            }

            float otherAlong = (groupTarget.x - positions[i].x) * flowForward.x
                + (groupTarget.y - positions[i].y) * flowForward.y;
            if (otherAlong >= myAlong)
            {
                continue;
            }

            float otherLane = (positions[i].x - groupTarget.x) * sideX + (positions[i].y - groupTarget.y) * sideY;
            float laneDelta = myLane - otherLane;
            if (laneDelta < 0f)
            {
                laneDelta = -laneDelta;
            }

            if (laneDelta <= yieldLaneWidth)
            {
                mustYield = true;
                break;
            }
        }

        if (!mustYield)
        {
            return;
        }

        _seek.x *= yieldSpeedScale;
        _seek.y *= yieldSpeedScale;
        _separation.x *= yieldSpeedScale;
        _separation.y *= yieldSpeedScale;
    }

    private void ApplyObstacleAvoidance(Vector2 position)
    {
        if (obstacleMask.value == 0 || obstacleCheckDistance <= 0f)
        {
            return;
        }

        float sqr = _desired.sqrMagnitude;
        if (sqr <= 0.0001f)
        {
            return;
        }

        float invLen = 1f / Mathf.Sqrt(sqr);
        Vector2 dir;
        dir.x = _desired.x * invLen;
        dir.y = _desired.y * invLen;

        _obstacleFilter.SetLayerMask(obstacleMask);
        int hits = Physics2D.CircleCast(
            position,
            groundCheckRadius,
            dir,
            _obstacleFilter,
            _obstacleHits,
            obstacleCheckDistance);

        if (hits <= 0)
        {
            return;
        }

        Vector2 normal = _obstacleHits[0].normal;
        float avoidX = normal.x * obstacleAvoidWeight * maxSpeed * _speedScale;
        float avoidY = normal.y * obstacleAvoidWeight * maxSpeed * _speedScale;
        _desired.x += avoidX;
        _desired.y += avoidY;
    }

    private void AccelerateTowardDesired(float dt)
    {
        float personalMax = maxSpeed * _speedScale;
        float desiredSqr = _desired.sqrMagnitude;
        float maxSqr = personalMax * personalMax;
        if (desiredSqr > maxSqr && desiredSqr > 0.0001f)
        {
            float inv = personalMax / Mathf.Sqrt(desiredSqr);
            _desired.x *= inv;
            _desired.y *= inv;
        }

        float rate = _desired.sqrMagnitude > _velocity.sqrMagnitude
            ? acceleration * _accelScale * (1f + _moveIntent * 0.85f)
            : deceleration * _accelScale * (0.65f + (1f - _moveIntent) * 0.35f);

        float step = rate * dt;
        _velocity.x = Mathf.MoveTowards(_velocity.x, _desired.x, step);
        _velocity.y = Mathf.MoveTowards(_velocity.y, _desired.y, step);
    }

    private void ApplyMovement(Vector2 position, float dt)
    {
        _nextPosition.x = position.x + _velocity.x * dt;
        _nextPosition.y = position.y + _velocity.y * dt;

        if (groundMask.value != 0 && _isGrounded && !IsOnGround(_nextPosition))
        {
            float risk = _velocity.magnitude + _moveIntent * maxSpeed;
            if (risk < carefulLeaveSpeed * 0.55f)
            {
                Vector2 slideX;
                slideX.x = _nextPosition.x;
                slideX.y = position.y;
                Vector2 slideY;
                slideY.x = position.x;
                slideY.y = _nextPosition.y;

                if (IsOnGround(slideX))
                {
                    _nextPosition = slideX;
                    _velocity.y *= 0.2f;
                }
                else if (IsOnGround(slideY))
                {
                    _nextPosition = slideY;
                    _velocity.x *= 0.2f;
                }
                else
                {
                    _nextPosition = position;
                    _velocity.x *= 0.4f;
                    _velocity.y *= 0.4f;
                }

                // #region agent log
                if (_debugFallLogs < 40 && Time.frameCount % 30 == 0)
                {
                    try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix6\",\"hypothesisId\":\"H\",\"location\":\"Creep.cs:ApplyMovement\",\"message\":\"careful edge stay\",\"data\":{\"name\":\"" + name + "\",\"risk\":" + risk.ToString("R") + ",\"moveIntent\":" + _moveIntent.ToString("R") + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
                }
                // #endregion
            }
        }

        if (body != null)
        {
            body.MovePosition(_nextPosition);
            return;
        }

        _transform.position = _nextPosition;
    }

    private void ApplyWalkBob()
    {
        float spd = _velocity.magnitude;
        if (spd < 0.08f)
        {
            _transform.localScale = _baseScale;
            _transform.localRotation = Quaternion.identity;
            return;
        }

        float wave = Mathf.Sin(Time.time * walkBobSpeed + _bobPhase);
        float amount = Mathf.Clamp01(spd / (maxSpeed * 0.65f));
        float bob = wave * walkBobAmplitude * amount;
        Vector3 scale = _baseScale;
        scale.x = _baseScale.x * (1f + bob);
        scale.y = _baseScale.y * (1f - bob * 0.7f);
        _transform.localScale = scale;
        _transform.localRotation = Quaternion.Euler(0f, 0f, wave * walkBobAngle * amount);

        // #region agent log
        _bobLogTimer += Time.deltaTime;
        if (_bobLogTimer >= 0.5f && name == "Square")
        {
            _bobLogTimer = 0f;
            try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix6\",\"hypothesisId\":\"I\",\"location\":\"Creep.cs:ApplyWalkBob\",\"message\":\"walk bob\",\"data\":{\"spd\":" + spd.ToString("R") + ",\"bob\":" + bob.ToString("R") + ",\"angle\":" + (wave * walkBobAngle * amount).ToString("R") + ",\"amount\":" + amount.ToString("R") + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
        }
        // #endregion
    }

    private void BeginFall()
    {
        if (_isFalling)
        {
            return;
        }

        _isFalling = true;
        _fallScale = _baseScale;
        _transform.localScale = _baseScale;
        _transform.localRotation = Quaternion.identity;
        _velocity = Vector2.zero;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.simulated = false;
        }

        if (bodyCollider != null)
        {
            bodyCollider.enabled = false;
        }
    }

    private void TickFallScale(float dt)
    {
        float step = fallScaleSpeed * dt;
        _fallScale.x = Mathf.MoveTowards(_fallScale.x, minFallScale, step);
        _fallScale.y = Mathf.MoveTowards(_fallScale.y, minFallScale, step);
        _fallScale.z = Mathf.MoveTowards(_fallScale.z, minFallScale, step);
        _transform.localScale = _fallScale;

        if (_fallScale.x <= minFallScale + 0.0001f)
        {
            gameObject.SetActive(false);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isFalling || other == null)
        {
            return;
        }

        if (!IsInLayerMask(other.gameObject.layer, abyssMask))
        {
            return;
        }

        // #region agent log
        if (_debugFallLogs < 30)
        {
            _debugFallLogs++;
            Vector2 p = _transform.position;
            try { System.IO.File.AppendAllText(@"D:\Project\Survivor\debug-b47418.log", "{\"sessionId\":\"b47418\",\"runId\":\"post-fix3\",\"hypothesisId\":\"B\",\"location\":\"Creep.cs:OnTriggerEnter2D\",\"message\":\"abyss trigger fall\",\"data\":{\"name\":\"" + name + "\",\"px\":" + p.x.ToString("R") + ",\"py\":" + p.y.ToString("R") + ",\"vx\":" + _velocity.x.ToString("R") + ",\"vy\":" + _velocity.y.ToString("R") + ",\"spd\":" + _velocity.magnitude.ToString("R") + ",\"other\":\"" + other.name + "\",\"otherLayer\":" + other.gameObject.layer + ",\"abyssMask\":" + abyssMask.value + ",\"grounded\":" + (_isGrounded ? "true" : "false") + "},\"timestamp\":" + System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}\n"); } catch {}
        }
        // #endregion
        BeginFall();
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}
