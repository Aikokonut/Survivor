using UnityEngine;

public class Creep : MonoBehaviour
{
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private Collider2D bodyCollider;
    [SerializeField] private GameObject leaderArrow;
    [SerializeField] private float maxSpeed = 4.5f;
    [SerializeField] private float acceleration = 16f;
    [SerializeField] private float deceleration = 11f;
    [SerializeField] private float idleDeceleration = 5.5f;
    [SerializeField] private float slotFollowWeight = 1f;
    [SerializeField] private float obstacleCheckDistance = 0.55f;
    [SerializeField] private float obstacleAvoidWeight = 1.4f;
    [SerializeField] private float groundCheckRadius = 0.05f;
    [SerializeField] private float fallGraceTime = 0.06f;
    [SerializeField] private float catchUpDistance = 1.4f;
    [SerializeField] private float walkBobAmplitude = 10f;
    [SerializeField] private float walkBobSpeed = 22f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask abyssMask;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float fallScaleSpeed = 2.5f;
    [SerializeField] private float minFallScale = 0.05f;
    [SerializeField] private float idleSettleSpeed = 0.85f;
    [SerializeField] private float idleSlotMaxSpeed = 2.4f;
    [SerializeField] private float idleArriveRadius = 0.55f;
    [SerializeField] private float bodyRadius = 0.22f;
    [SerializeField] private float partingLaneWidth = 0.4f;
    [SerializeField] private float partingWeight = 1.2f;
    [SerializeField] private float leaderClearRadius = 0.55f;
    [SerializeField] private float moveSeekWeight = 0.48f;
    [SerializeField] private float moveTurnAccelScale = 1.55f;
    [SerializeField] private float leaderDriveScale = 0.97f;
    [SerializeField] private float followerBehindPull = 1.2f;
    [SerializeField] private float followerBackBias = 0.35f;

    private Transform _transform;
    private Vector2 _formationOffset;
    private Vector2 _velocity;
    private Vector2 _desired;
    private Vector3 _baseScale;
    private Vector3 _baseEuler;
    private Vector3 _fallScale;
    private readonly Collider2D[] _groundHits = new Collider2D[1];
    private readonly RaycastHit2D[] _obstacleHits = new RaycastHit2D[4];
    private readonly RaycastHit2D[] _slideHits = new RaycastHit2D[4];
    private ContactFilter2D _groundFilter;
    private ContactFilter2D _obstacleFilter;
    private float _airTime;
    private float _bobPhase;
    private float _moveIntent;
    private float _idleSpeedCap;
    private bool _isGrounded;
    private bool _isFalling;
    private bool _isDead;
    private bool _isLeader;
    private bool _configured;
    private bool _hasGroundMask;
    private bool _hasObstacleMask;
    private bool _groundChecked;

    public event System.Action<Creep> onDied;

    private void Awake()
    {
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
        }

        if (leaderArrow != null) leaderArrow.SetActive(_isLeader);
    }

    private void OnEnable()
    {
        _isDead = false;
        _isFalling = false;
    }

    private void OnDisable()
    {
        if (_isDead) return;
        _isDead = true;
        SetLeader(false);
        if (onDied != null) onDied.Invoke(this);
    }

    public void ConfigureSwarmMember(Vector2 formationOffset)
    {
        _formationOffset = formationOffset;
        _configured = true;
    }

    public Vector2 GetPosition() { return _transform.position; }
    public LayerMask GetGroundMask() { return groundMask; }
    public bool IsFalling() { return _isFalling; }
    public bool IsDead() { return _isDead || _isFalling; }
    public bool IsLeader() { return _isLeader; }

    public void SetLeader(bool isLeader)
    {
        _isLeader = isLeader;
        if (leaderArrow != null) leaderArrow.SetActive(isLeader);
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
        Vector2 leaderPosition,
        int leaderIndex)
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
        Vector2 position = _transform.position;
        BuildDesired(position, groupTarget, flowForward, moveInput, formationScale, positions, count, selfIndex, separationRadius, separationWeight, leaderPosition);
        AvoidObstacles(position);
        IntegrateVelocity(dt);
        Move(position, dt, positions, count, selfIndex);
        if (positions != null && selfIndex >= 0 && selfIndex < count) positions[selfIndex] = _transform.position;
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

    private void BuildDesired(
        Vector2 position,
        Vector2 groupTarget,
        Vector2 flowForward,
        Vector2 moveInput,
        float formationScale,
        Vector2[] positions,
        int count,
        int selfIndex,
        float sepRadius,
        float sepWeight,
        Vector2 leaderPos)
    {
        bool moving = _moveIntent > 0.05f;

        if (_isLeader)
        {
            _idleSpeedCap = idleSlotMaxSpeed;
            _desired = moving ? moveInput * (maxSpeed * leaderDriveScale) : Vector2.zero;
            return;
        }

        Vector2 sep = SoftSep(position, positions, count, selfIndex, sepRadius, sepWeight);

        if (!moving)
        {
            Vector2 idleForward = flowForward.sqrMagnitude > 0.0001f ? flowForward.normalized : Vector2.up;
            Vector2 toSlot = groupTarget + _formationOffset - position;
            float distSqr = toSlot.sqrMagnitude;
            float arriveSqr = idleArriveRadius * idleArriveRadius;
            _idleSpeedCap = distSqr > arriveSqr ? maxSpeed : idleSlotMaxSpeed;

            if (distSqr < 0.008f)
            {
                _desired = SidePush(position, leaderPos, idleForward, idleSlotMaxSpeed) + sep * 0.25f;
                return;
            }

            if (distSqr <= arriveSqr && _velocity.sqrMagnitude > idleSettleSpeed * idleSettleSpeed)
            {
                _desired = Vector2.zero;
                return;
            }

            _desired = Vector2.ClampMagnitude(toSlot * (slotFollowWeight * 1.35f), _idleSpeedCap)
                + sep * 0.25f
                + SidePush(position, leaderPos, idleForward, _idleSpeedCap);
            return;
        }

        _idleSpeedCap = idleSlotMaxSpeed;

        Vector2 forward = flowForward.sqrMagnitude > 0.0001f ? flowForward.normalized : Vector2.up;
        Vector2 side = new Vector2(-forward.y, forward.x);
        float lat = Vector2.Dot(_formationOffset, side);
        float alongOff = Vector2.Dot(_formationOffset, forward) * 0.5f - followerBackBias;
        Vector2 slot = groupTarget + side * (lat * formationScale) + forward * (alongOff * formationScale);

        Vector2 drive = moveInput * maxSpeed;
        float along = Vector2.Dot(position - leaderPos, forward);
        if (along > 0f) drive -= forward * (along * followerBehindPull * maxSpeed * 0.25f);

        Vector2 seek = Vector2.ClampMagnitude((slot - position) * (slotFollowWeight * moveSeekWeight), maxSpeed * 0.3f);
        Vector2 parting = SidePush(position, leaderPos, forward, maxSpeed);
        _desired = drive + seek + sep * 0.5f + parting;

        Vector2 toGroup = groupTarget - position;
        float far = catchUpDistance * 2.8f;
        if (toGroup.sqrMagnitude > far * far)
        {
            _desired += toGroup * (maxSpeed * 0.22f / Mathf.Sqrt(toGroup.sqrMagnitude));
        }
    }

    private Vector2 SidePush(Vector2 position, Vector2 leaderPos, Vector2 forward, float personalMax)
    {
        Vector2 delta = position - leaderPos;
        float sqr = delta.sqrMagnitude;
        if (sqr <= 0.000001f) return Vector2.zero;

        Vector2 side = new Vector2(-forward.y, forward.x);
        float along = Vector2.Dot(delta, forward);
        float lat = Vector2.Dot(delta, side);
        float absLat = Mathf.Abs(lat);
        float strength = 0f;

        if (along > -0.45f && along < 1.25f && absLat < partingLaneWidth)
        {
            float t = (1f - Mathf.Clamp01((along + 0.45f) / 1.7f)) * (1f - absLat / partingLaneWidth);
            strength = t * partingWeight * personalMax;
        }

        float clearSqr = leaderClearRadius * leaderClearRadius;
        if (sqr < clearSqr)
        {
            float d = Mathf.Sqrt(sqr);
            strength = Mathf.Max(strength, ((leaderClearRadius - d) / leaderClearRadius) * 2.2f * personalMax);
        }

        if (strength <= 0f) return Vector2.zero;

        Vector2 away = delta / Mathf.Sqrt(sqr);
        float sign = lat >= 0f ? 1f : -1f;
        if (absLat < 0.04f) sign = Vector2.Dot(_formationOffset, side) >= 0f ? 1f : -1f;
        Vector2 dir = (away * 0.7f + side * (sign * 0.3f));
        float dirMag = dir.magnitude;
        if (dirMag <= 0.0001f) return away * strength;
        return dir * (strength / dirMag);
    }

    private Vector2 SoftSep(Vector2 position, Vector2[] positions, int count, int selfIndex, float radius, float weight)
    {
        if (positions == null || count <= 0 || radius <= 0f) return Vector2.zero;

        float sepSqr = radius * radius;
        Vector2 push = Vector2.zero;
        for (int i = 0; i < count; i++)
        {
            if (i == selfIndex) continue;
            Vector2 d = position - positions[i];
            float sqr = d.sqrMagnitude;
            if (sqr <= 0.0001f || sqr >= sepSqr) continue;
            float dist = Mathf.Sqrt(sqr);
            push += d * ((((radius - dist) / radius) * weight * maxSpeed) / dist);
        }

        float maxPush = maxSpeed * 1.1f;
        if (push.sqrMagnitude > maxPush * maxPush) push = push.normalized * maxPush;
        return push;
    }

    private void AvoidObstacles(Vector2 position)
    {
        if (!_hasObstacleMask || obstacleCheckDistance <= 0f || _desired.sqrMagnitude <= 0.0001f) return;

        Vector2 dir = _desired.normalized;
        int count = Physics2D.CircleCast(position, bodyRadius, dir, _obstacleFilter, _obstacleHits, obstacleCheckDistance);
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = _obstacleHits[i];
            if (hit.collider == null) continue;
            float into = Vector2.Dot(_desired, hit.normal);
            if (into >= 0f) continue;

            _desired -= hit.normal * into;
            if (_moveIntent > 0.05f)
            {
                Vector2 tangent = new Vector2(-hit.normal.y, hit.normal.x);
                float sign = Vector2.Dot(_desired, tangent) >= 0f ? 1f : -1f;
                _desired += tangent * (sign * obstacleAvoidWeight * maxSpeed * 0.45f);
            }
            break;
        }
    }

    private void IntegrateVelocity(float dt)
    {
        float cap = _moveIntent > 0.05f ? maxSpeed * (_isLeader ? leaderDriveScale : 1f) : _idleSpeedCap;
        _desired = Vector2.ClampMagnitude(_desired, cap);

        float rate = _desired.sqrMagnitude > _velocity.sqrMagnitude
            ? acceleration * (_moveIntent > 0.05f ? moveTurnAccelScale : 1f)
            : (_moveIntent > 0.05f ? deceleration : idleDeceleration);

        _velocity = Vector2.MoveTowards(_velocity, _desired, rate * dt);
        if (_moveIntent <= 0.05f && _velocity.sqrMagnitude < 0.0025f && _desired.sqrMagnitude < 0.0025f)
        {
            _velocity = Vector2.zero;
        }
    }

    private void Move(Vector2 position, float dt, Vector2[] positions, int count, int selfIndex)
    {
        Vector2 delta = _velocity * dt;
        float dist = delta.magnitude;

        if (dist > 0.0001f && _hasObstacleMask)
        {
            Vector2 dir = delta / dist;
            int hits = body != null
                ? body.Cast(dir, _obstacleFilter, _obstacleHits, dist + 0.02f)
                : Physics2D.CircleCast(position, bodyRadius, dir, _obstacleFilter, _obstacleHits, dist + 0.02f);

            for (int i = 0; i < hits; i++)
            {
                RaycastHit2D h = _obstacleHits[i];
                if (h.collider == null || Vector2.Dot(dir, h.normal) >= -0.001f) continue;

                Vector2 move = dir * Mathf.Max(0f, h.distance - 0.005f);
                Vector2 remaining = delta - move;
                float into = Vector2.Dot(_velocity, h.normal);
                if (into < 0f) _velocity -= h.normal * into;
                float remInto = Vector2.Dot(remaining, h.normal);
                if (remInto < 0f) remaining -= h.normal * remInto;

                float remDist = remaining.magnitude;
                if (remDist > 0.0001f)
                {
                    Vector2 remDir = remaining / remDist;
                    int slideHits = body != null
                        ? body.Cast(remDir, _obstacleFilter, _slideHits, remDist + 0.01f)
                        : Physics2D.CircleCast(position + move, bodyRadius, remDir, _obstacleFilter, _slideHits, remDist + 0.01f);
                    float slideSafe = remDist;
                    for (int s = 0; s < slideHits; s++)
                    {
                        if (_slideHits[s].collider == null || Vector2.Dot(remDir, _slideHits[s].normal) >= -0.001f) continue;
                        slideSafe = Mathf.Min(slideSafe, Mathf.Max(0f, _slideHits[s].distance - 0.005f));
                    }
                    move += remDir * slideSafe;
                }

                if (move.sqrMagnitude < 0.00001f) _velocity = Vector2.zero;
                delta = move;
                break;
            }
        }

        Vector2 next = position + delta;
        if (!_isLeader) ResolveOverlap(ref next, positions, count, selfIndex);

        if (_hasObstacleMask)
        {
            Vector2 push = next - position;
            float pushDist = push.magnitude;
            if (pushDist > 0.0001f)
            {
                Vector2 dir = push / pushDist;
                int hits = body != null
                    ? body.Cast(dir, _obstacleFilter, _obstacleHits, pushDist + 0.02f)
                    : Physics2D.CircleCast(position, bodyRadius, dir, _obstacleFilter, _obstacleHits, pushDist + 0.02f);
                for (int i = 0; i < hits; i++)
                {
                    RaycastHit2D h = _obstacleHits[i];
                    if (h.collider == null || Vector2.Dot(dir, h.normal) >= -0.001f) continue;
                    next = position + dir * Mathf.Max(0f, h.distance - 0.005f);
                    break;
                }
            }
        }

        if (body != null) body.MovePosition(next);
        else _transform.position = next;
        if (_hasGroundMask && !IsOnGround(next)) _isGrounded = false;
    }

    private void ResolveOverlap(ref Vector2 position, Vector2[] positions, int count, int selfIndex)
    {
        if (positions == null) return;
        float minDist = bodyRadius * 2f;
        float minSqr = minDist * minDist;
        for (int i = 0; i < count; i++)
        {
            if (i == selfIndex) continue;
            Vector2 d = position - positions[i];
            float sqr = d.sqrMagnitude;
            if (sqr >= minSqr || sqr <= 0.000001f) continue;
            float dist = Mathf.Sqrt(sqr);
            position += d * (((minDist - dist) * 0.5f) / dist);
        }
    }

    private void ApplyWalkBob()
    {
        float angle = _baseEuler.z;
        float spd = _velocity.magnitude;
        if (spd >= 0.08f)
        {
            angle += Mathf.Sin(Time.time * walkBobSpeed + _bobPhase) * walkBobAmplitude * Mathf.Clamp01(spd / maxSpeed);
        }
        if (body != null) body.rotation = angle;
        else _transform.localEulerAngles = new Vector3(_baseEuler.x, _baseEuler.y, angle);
    }

    private void BeginFall()
    {
        if (_isFalling) return;
        _isFalling = true;
        if (!_isDead)
        {
            _isDead = true;
            SetLeader(false);
            if (onDied != null) onDied.Invoke(this);
        }
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
        else _transform.localEulerAngles = _baseEuler;
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
