using UnityEngine;
using UnityEngine.EventSystems;

public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public delegate void InputChangedHandler(Vector2 input);

    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private float handleRange = 1f;
    [SerializeField] private float deadZone;
    [SerializeField] private float smoothSpeed = 18f;
    [SerializeField] private float releaseSmoothSpeed = 22f;

    private RectTransform _baseRect;
    private Canvas _canvas;
    private Camera _eventCamera;
    private Vector2 _rawInput;
    private Vector2 _smoothedInput;
    private Vector2 _lastScreenPoint;
    private Vector2 _radius;
    private InputChangedHandler _onInputChanged;
    private bool _isPressed;
    private bool _wasSending;

    private void Awake()
    {
        _baseRect = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        _rawInput = Vector2.zero;
        _smoothedInput = Vector2.zero;
        _lastScreenPoint = Vector2.zero;
        _isPressed = false;
        _wasSending = false;

        if (background != null)
        {
            _radius.x = background.sizeDelta.x * 0.5f;
            _radius.y = background.sizeDelta.y * 0.5f;
        }

        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }

        ResolveEventCamera();
    }

    private void Update()
    {
        if (!_isPressed && !_wasSending && _smoothedInput.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        if (_isPressed)
        {
            ApplyScreenPoint(_lastScreenPoint);
        }

        float speed = _isPressed ? smoothSpeed : releaseSmoothSpeed;
        float t = 1f - Mathf.Exp(-Mathf.Max(speed, 0.01f) * Time.unscaledDeltaTime);
        _smoothedInput.x = Mathf.Lerp(_smoothedInput.x, _rawInput.x, t);
        _smoothedInput.y = Mathf.Lerp(_smoothedInput.y, _rawInput.y, t);

        if (!_isPressed && _smoothedInput.sqrMagnitude < 0.0001f)
        {
            _smoothedInput.x = 0f;
            _smoothedInput.y = 0f;
        }

        if (handle != null)
        {
            handle.anchoredPosition = new Vector2(
                _smoothedInput.x * _radius.x * handleRange,
                _smoothedInput.y * _radius.y * handleRange);
        }

        bool shouldSend = _isPressed
            || _smoothedInput.sqrMagnitude > 0.0001f
            || _rawInput.sqrMagnitude > 0.0001f;

        if (_onInputChanged != null && (shouldSend || _wasSending))
        {
            _onInputChanged(_smoothedInput);
        }

        _wasSending = shouldSend;
    }

    public void AddInputListener(InputChangedHandler handler)
    {
        _onInputChanged += handler;
    }

    public void RemoveInputListener(InputChangedHandler handler)
    {
        _onInputChanged -= handler;
    }

    public void GetInput(out float x, out float y)
    {
        x = _smoothedInput.x;
        y = _smoothedInput.y;
    }

    public Vector2 GetInput()
    {
        return _smoothedInput;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        _lastScreenPoint = eventData.position;
        ApplyScreenPoint(_lastScreenPoint);
    }

    public void OnDrag(PointerEventData eventData)
    {
        _lastScreenPoint = eventData.position;
        ApplyScreenPoint(_lastScreenPoint);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        _rawInput.x = 0f;
        _rawInput.y = 0f;
    }

    private void ApplyScreenPoint(Vector2 screenPoint)
    {
        if (_baseRect == null || background == null)
        {
            return;
        }

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, screenPoint, _eventCamera, out localPoint))
        {
            return;
        }

        if (_radius.x > 0.0001f)
        {
            localPoint.x /= _radius.x;
        }

        if (_radius.y > 0.0001f)
        {
            localPoint.y /= _radius.y;
        }

        _rawInput = localPoint;
        float magnitude = _rawInput.magnitude;
        if (magnitude <= deadZone)
        {
            _rawInput.x = 0f;
            _rawInput.y = 0f;
            return;
        }

        if (magnitude > 1f)
        {
            _rawInput.x /= magnitude;
            _rawInput.y /= magnitude;
        }
    }

    private void ResolveEventCamera()
    {
        if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            _eventCamera = null;
            return;
        }

        _eventCamera = _canvas.worldCamera;
    }
}
