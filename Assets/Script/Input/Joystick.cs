using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public delegate void InputChangedHandler(Vector2 input);

    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private RectTransform touchArea;
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
    private TouchAreaRelay _touchAreaRelay;
    private CanvasGroup _canvasGroup;
    private bool _hideViaCanvasGroup;

    private void Awake()
    {
        _baseRect = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        _rawInput = Vector2.zero;
        _smoothedInput = Vector2.zero;
        _lastScreenPoint = Vector2.zero;
        _isPressed = false;
        _wasSending = false;
        _hideViaCanvasGroup = false;

        if (touchArea == null)
        {
            touchArea = _baseRect;
        }

        if (background != null)
        {
            _radius.x = background.sizeDelta.x * 0.5f;
            _radius.y = background.sizeDelta.y * 0.5f;
            _hideViaCanvasGroup = background.gameObject == gameObject;
            if (!_hideViaCanvasGroup)
            {
                DisableRaycasts(background);
            }
        }

        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }

        if (_hideViaCanvasGroup)
        {
            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;
        }

        ResolveEventCamera();
        SetupTouchAreaRelay();
        SetVisible(false);
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

        if (handle != null && IsVisualVisible())
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
        HandlePointerDown(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        HandleDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        HandlePointerUp(eventData);
    }

    private void HandlePointerDown(PointerEventData eventData)
    {
        if (!IsInsideTouchArea(eventData.position))
        {
            return;
        }

        _isPressed = true;
        _lastScreenPoint = eventData.position;
        PositionAtScreenPoint(_lastScreenPoint);
        SetVisible(true);
        ApplyScreenPoint(_lastScreenPoint);
    }

    private void HandleDrag(PointerEventData eventData)
    {
        if (!_isPressed)
        {
            return;
        }

        _lastScreenPoint = eventData.position;
        ApplyScreenPoint(_lastScreenPoint);
    }

    private void HandlePointerUp(PointerEventData eventData)
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        _rawInput.x = 0f;
        _rawInput.y = 0f;

        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }

        SetVisible(false);
    }

    private void SetupTouchAreaRelay()
    {
        if (touchArea == null)
        {
            return;
        }

        Graphic graphic = touchArea.GetComponent<Graphic>();
        if (graphic == null)
        {
            Image image = touchArea.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            graphic = image;
        }

        graphic.raycastTarget = true;

        if (touchArea.gameObject == gameObject)
        {
            return;
        }

        _touchAreaRelay = touchArea.GetComponent<TouchAreaRelay>();
        if (_touchAreaRelay == null)
        {
            _touchAreaRelay = touchArea.gameObject.AddComponent<TouchAreaRelay>();
        }

        _touchAreaRelay.Bind(this);
    }

    private void DisableRaycasts(RectTransform root)
    {
        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        int i = 0;
        int count = graphics.Length;
        while (i < count)
        {
            graphics[i].raycastTarget = false;
            i++;
        }
    }

    private void PositionAtScreenPoint(Vector2 screenPoint)
    {
        if (background == null)
        {
            return;
        }

        RectTransform parent = background.parent as RectTransform;
        if (parent == null)
        {
            return;
        }

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, _eventCamera, out localPoint))
        {
            return;
        }

        background.anchoredPosition = localPoint;
    }

    private bool IsInsideTouchArea(Vector2 screenPoint)
    {
        if (touchArea == null)
        {
            return true;
        }

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(touchArea, screenPoint, _eventCamera, out localPoint))
        {
            return false;
        }

        return touchArea.rect.Contains(localPoint);
    }

    private bool IsVisualVisible()
    {
        if (_hideViaCanvasGroup)
        {
            return _canvasGroup != null && _canvasGroup.alpha > 0.001f;
        }

        return background != null && background.gameObject.activeSelf;
    }

    private void SetVisible(bool visible)
    {
        if (background == null)
        {
            return;
        }

        if (_hideViaCanvasGroup)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
            }

            return;
        }

        background.gameObject.SetActive(visible);
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

    private sealed class TouchAreaRelay : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private Joystick _owner;

        public void Bind(Joystick owner)
        {
            _owner = owner;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.HandlePointerDown(eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.HandleDrag(eventData);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_owner != null)
            {
                _owner.HandlePointerUp(eventData);
            }
        }
    }
}
