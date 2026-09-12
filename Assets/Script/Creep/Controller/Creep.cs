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
    [SerializeField] private float groundCheckRadius = 0.05f;
    [SerializeField] private float fallGraceTime = 0.06f;
    [SerializeField] private float carefulLeaveSpeed = 1.6f;
    [SerializeField] private float catchUpDistance = 1.4f;
    [SerializeField] private float catchUpBoost = 1.75f;
    [SerializeField] private float walkBobAmplitude = 10f;
    [SerializeField] private float walkBobSpeed = 22f;
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
    private Vector2 _groupTarget;
    private Vector3 _baseScale;
    private Vector3 _baseEuler;
    private Vector3 _fallScale;
    private readonly Collider2D[] _groundHits = new Collider2D[1];
    private readonly RaycastHit2D[] _obstacleHits = new RaycastHit2D[4];
    private readonly RaycastHit2D[] _slideHits = new RaycastHit2D[4];
    private ContactFilter2D _groundFilter;
    private ContactFilter2D _obstacleFilter;
    private float _speedScale = 1f;
    private float _accelScale = 1f;
    private float _airTime;
    private float _bobPhase;
    private float _moveIntent;
    private bool _isGrounded;
    private bool _isFalling;
    private bool _configured;
    private bool _hasGroundMask;
    private bool _hasObstacleMask;
    private bool _groundChecked;
    private bool _evenSpacingMovement;

    private void Awake()
    {
        if (groundCheckRadius > 0.08f) groundCheckRadius = 0.05f;
        if (fallGraceTime > 0.08f) fallGraceTime = 0.06f;

        _transform = transform;
        _baseScale = _transform.localScale;
        _baseEuler = _transform.localEulerAngles;
        _fallScale = _baseScale;
        _bobPhase = Mathf.Abs(GetInstanceID() % 628) * 0.01f;
        _hasGroundMask = groundMask.value != 0;
        _hasObstacleMask = obstacleMask.value != 0;

        _groundFilter = new ContactFilter2D { useTriggers = true, useLayerMask = true };
        _groundFilter.SetLayerMask(groundMask);

        _obstacleFilter = new ContactFilter2D { useTriggers = false, useLayerMask = true };
        _obstacleFilter.SetLayerMask(obstacleMask);

        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.simulated = true;
            body.gravityScale = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.constraints = RigidbodyConstraints2D.None;
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
        Vector2 moveInput,
        float formationScale,
        Vector2[] positions,
        int count,
        int selfIndex,
        float separationRadius,
        float separationWeight,
        float moveIntent,
        bool evenSpacingMovement = true)
    {
        if (_isFalling) { TickFallScale(dt); return; }
        if (!_configured) return;

        if (!_groundChecked)
        {
            _isGrounded = IsOnGround(_transform.position);
            _groundChecked = true;
        }

        if (_hasGroundMask && !_isGrounded)
        {
            _velocity = Vector2.zero;
            UpdateFallState(dt);
            return;
        }

        _moveIntent = moveIntent;
        _groupTarget = groupTarget;
        _evenSpacingMovement = evenSpacingMovement;

        Vector2 position = _transform.position;
        BuildDesiredVelocity(position, groupTarget, flowForward, moveInput, formationScale, positions, count, selfIndex, separationRadius, separationWeight, evenSpacingMovement);
        ApplyObstacleAvoidance(position);
        AccelerateTowardDesired(dt);
        ApplyMovement(position, dt);
        ApplyWalkBob();
    }

    private bool IsOnGround(Vector2 position)
    {
        return !_hasGroundMask || Physics2D.OverlapCircle(position, groundCheckRadius, _groundFilter, _groundHits) > 0;
    }

    private void UpdateFallState(float dt)
    {
        if (!_hasGroundMask || _isGrounded) { _airTime = 0f; return; }
        _airTime += dt;
        if (_airTime >= fallGraceTime) BeginFall();
    }

    private void BuildDesiredVelocity(
        Vector2 position,
        Vector2 groupTarget,
        Vector2 flowForward,
        Vector2 moveInput,
        float formationScale,
        Vector2[] positions,
        int count,
        int selfIndex,
        float separationRadius,
        float separationWeight,
        bool evenSpacingMovement)
    {
        float effectiveSpeedScale = evenSpacingMovement ? 1f : _speedScale;
        float personalMax = maxSpeed * effectiveSpeedScale;
        bool moving = _moveIntent > 0.05f;
        _seek = Vector2.zero;
        _separation = Vector2.zero;

        float sideX = -flowForward.y;
        float sideY = flowForward.x;
        Vector2 side = new Vector2(sideX, sideY);
        float lateral = (position.x - groupTarget.x) * sideX + (position.y - groupTarget.y) * sideY;

        ApplySeparation(position, positions, count, selfIndex, separationRadius, separationWeight, personalMax);

        if (!moving)
        {
            if (evenSpacingMovement)
            {
                Vector2 idleSlot = groupTarget + _formationOffset;
                Vector2 toIdleSlot = idleSlot - position;
                _seek = Vector2.ClampMagnitude(toIdleSlot * (slotFollowWeight * 1.0f), personalMax * 0.45f);
                _desired = _seek + _separation;
                return;
            }

            float stopPushLimit = personalMax * 0.45f;
            float sepSqr = _separation.sqrMagnitude;
            if (sepSqr > stopPushLimit * stopPushLimit && sepSqr > 0.0001f)
            {
                _separation *= (stopPushLimit / Mathf.Sqrt(sepSqr));
            }
            float bridgeHold = lateral * (1f - formationScale) * 0.65f;
            _desired = _separation - side * bridgeHold;
            return;
        }

        float compress = Mathf.Max(0f, 1f - formationScale);

        if (evenSpacingMovement)
        {
            Vector2 moveSlot = groupTarget + _formationOffset;
            Vector2 toMoveSlot = moveSlot - position;
            _seek = Vector2.ClampMagnitude(toMoveSlot * (slotFollowWeight * 1.0f), personalMax * 0.45f);

            _desired = moveInput * personalMax + _seek + _separation;

            Vector2 toGroup = groupTarget - position;
            float toGroupSqr = toGroup.sqrMagnitude;
            float farBehindDist = catchUpDistance * 2.2f;
            if (toGroupSqr > farBehindDist * farBehindDist)
            {
                _desired += toGroup * (personalMax * 0.4f / Mathf.Sqrt(toGroupSqr));
            }
            return;
        }

        personalMax *= (1f + _moveIntent * 0.35f);

        float rawLat = _formationOffset.x * sideX + _formationOffset.y * sideY;
        float rawAlong = _formationOffset.x * flowForward.x + _formationOffset.y * flowForward.y;
        float latOff = rawLat * formationScale;
        float alongOff = rawAlong * Mathf.Lerp(1f, 2.5f, compress) - rawLat * (compress * 1.2f);
        Vector2 slot = groupTarget + side * latOff + flowForward * alongOff;

        Vector2 toSlot = slot - position;
        Vector2 toTarget = groupTarget - position;
        float blend = Mathf.Lerp(0.15f, 0.35f, compress);
        Vector2 seek = Vector2.Lerp(toSlot, toTarget, blend);
        float seekSqr = seek.sqrMagnitude;

        if (seekSqr > 0.0001f)
        {
            float dist = Mathf.Sqrt(seekSqr);
            float arrive = Mathf.Lerp(0.2f, arriveRadius * 0.65f, formationScale) * 0.35f;
            float speed = dist < arrive ? personalMax * Mathf.Max(0.7f, dist / Mathf.Max(arrive, 0.05f)) : personalMax;
            _seek = seek * ((speed / dist) * slotFollowWeight * (0.55f + _moveIntent * 0.55f));
        }

        float funnelLegacy = Mathf.Clamp(lateral * 1.5f, -1f, 1f) * (personalMax * 0.4f * compress);
        Vector2 funnelForce = side * funnelLegacy;
        _seek -= funnelForce;

        float driveScaleLegacy = Mathf.Lerp(0.55f, 1f, formationScale);
        _desired = moveInput * (personalMax * driveScaleLegacy) + _seek * 0.65f + _separation;

        Vector2 toGroupLegacy = groupTarget - position;
        float toGroupLegacySqr = toGroupLegacy.sqrMagnitude;
        if (toGroupLegacySqr > catchUpDistance * catchUpDistance)
        {
            _desired += toGroupLegacy * (catchUpBoost * personalMax / Mathf.Sqrt(toGroupLegacySqr));
        }
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
        if (positions == null || count <= 0) return;

        float sepSqr = separationRadius * separationRadius;
        Vector2 push = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            if (i == selfIndex) continue;
            float dx = position.x - positions[i].x;
            if (dx > separationRadius || dx < -separationRadius) continue;
            float dy = position.y - positions[i].y;
            if (dy > separationRadius || dy < -separationRadius) continue;

            float sqr = dx * dx + dy * dy;
            if (sqr <= 0.0001f || sqr >= sepSqr) continue;

            float dist = Mathf.Sqrt(sqr);
            float inv = (((separationRadius - dist) / separationRadius) * separationWeight * personalMax) / dist;
            push.x += dx * inv;
            push.y += dy * inv;
        }

        float totalSepSqr = push.sqrMagnitude;
        float maxPush = personalMax * 1.35f;
        if (totalSepSqr > maxPush * maxPush && totalSepSqr > 0.0001f)
        {
            push *= (maxPush / Mathf.Sqrt(totalSepSqr));
        }

        _separation = push;
    }

    private void ApplyObstacleAvoidance(Vector2 position)
    {
        if (!_hasObstacleMask || obstacleCheckDistance <= 0f) return;
        float sqr = _desired.sqrMagnitude;
        if (sqr <= 0.0001f) return;

        Vector2 dir = _desired / Mathf.Sqrt(sqr);
        float bodyRadius = 0.22f;
        int count = Physics2D.CircleCast(position, bodyRadius, dir, _obstacleFilter, _obstacleHits, obstacleCheckDistance);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = _obstacleHits[i];
                if (hit.collider == null) continue;
                Vector2 norm = hit.normal;
                float into = Vector2.Dot(_desired, norm);
                if (into < 0f)
                {
                    _desired -= norm * into;
                    Vector2 tangent = new Vector2(-norm.y, norm.x);
                    float sideDot = Vector2.Dot(_desired, tangent);
                    float sign = sideDot >= 0f ? 1f : -1f;
                    if (Mathf.Abs(sideDot) < 0.05f)
                    {
                        sign = (_formationOffset.x * norm.y - _formationOffset.y * norm.x) >= 0f ? 1f : -1f;
                    }
                    _desired += tangent * (sign * (obstacleAvoidWeight * maxSpeed * _speedScale * 0.45f));
                    break;
                }
            }
        }
    }

    private void AccelerateTowardDesired(float dt)
    {
        float speedMul = _evenSpacingMovement ? 1f : (_speedScale * (1f + _moveIntent * 0.45f));
        float personalMax = maxSpeed * speedMul;
        _desired = Vector2.ClampMagnitude(_desired, personalMax);

        float accelMul = _evenSpacingMovement ? 1f : _accelScale;
        float rate = _desired.sqrMagnitude > _velocity.sqrMagnitude ? acceleration * accelMul : deceleration * accelMul;
        _velocity = Vector2.MoveTowards(_velocity, _desired, rate * dt);
    }

    private void ApplyMovement(Vector2 position, float dt)
    {
        Vector2 delta = _velocity * dt;
        float dist = delta.magnitude;

        if (dist > 0.0001f && _hasObstacleMask)
        {
            Vector2 dir = delta / dist;
            int hits = body != null
                ? body.Cast(dir, _obstacleFilter, _obstacleHits, dist + 0.02f)
                : Physics2D.CircleCast(position, 0.22f, dir, _obstacleFilter, _obstacleHits, dist + 0.02f);

            RaycastHit2D validHit = default;
            bool hasValidHit = false;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit2D h = _obstacleHits[i];
                if (h.collider == null) continue;
                if (Vector2.Dot(dir, h.normal) < -0.001f)
                {
                    validHit = h;
                    hasValidHit = true;
                    break;
                }
            }

            if (hasValidHit)
            {
                float safeDist = Mathf.Max(0f, validHit.distance - 0.005f);
                Vector2 move = dir * safeDist;
                Vector2 remaining = delta - move;

                float intoWall = Vector2.Dot(_velocity, validHit.normal);
                if (intoWall < 0f)
                {
                    _velocity -= validHit.normal * intoWall;
                }

                float remInto = Vector2.Dot(remaining, validHit.normal);
                if (remInto < 0f)
                {
                    remaining -= validHit.normal * remInto;
                }

                float remDist = remaining.magnitude;
                if (remDist > 0.0001f)
                {
                    Vector2 remDir = remaining / remDist;
                    int slideHits = body != null
                        ? body.Cast(remDir, _obstacleFilter, _slideHits, remDist + 0.01f)
                        : Physics2D.CircleCast(position + move, 0.22f, remDir, _obstacleFilter, _slideHits, remDist + 0.01f);

                    float slideSafe = remDist;
                    for (int s = 0; s < slideHits; s++)
                    {
                        RaycastHit2D sh = _slideHits[s];
                        if (sh.collider == null) continue;
                        if (Vector2.Dot(remDir, sh.normal) < -0.001f)
                        {
                            slideSafe = Mathf.Min(slideSafe, Mathf.Max(0f, sh.distance - 0.005f));
                        }
                    }
                    move += remDir * slideSafe;
                }

                if (move.sqrMagnitude < 0.00001f)
                {
                    _velocity = Vector2.zero;
                }

                delta = move;
            }
        }

        Vector2 next = position + delta;
        if (body != null) body.MovePosition(next);
        else _transform.position = next;

        if (_hasGroundMask && !IsOnGround(next))
        {
            _isGrounded = false;
        }
    }

    private void ApplyWalkBob()
    {
        float spd = _velocity.magnitude;
        float angle = _baseEuler.z;
        if (spd >= 0.08f)
        {
            float factor = Mathf.Clamp01(spd / maxSpeed);
            angle += Mathf.Sin(Time.time * walkBobSpeed + _bobPhase) * walkBobAmplitude * factor;
        }

        if (body != null) body.rotation = angle;
        else _transform.localEulerAngles = new Vector3(_baseEuler.x, _baseEuler.y, angle);
    }

    private void BeginFall()
    {
        if (_isFalling) return;

        _isFalling = true;
        _fallScale = _baseScale;
        _transform.localScale = _baseScale;
        _velocity = Vector2.zero;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.rotation = _baseEuler.z;
            body.simulated = false;
        }
        else
        {
            _transform.localEulerAngles = _baseEuler;
        }

        if (bodyCollider != null) bodyCollider.enabled = false;
    }

    private void TickFallScale(float dt)
    {
        _fallScale = Vector3.MoveTowards(_fallScale, Vector3.one * minFallScale, fallScaleSpeed * dt);
        _transform.localScale = _fallScale;
        if (_fallScale.x <= minFallScale + 0.0001f) gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isFalling || other == null) return;
        if ((abyssMask.value & (1 << other.gameObject.layer)) == 0) return;
        BeginFall();
    }
}
