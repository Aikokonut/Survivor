using UnityEngine;
using UnityEngine.EventSystems;

public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public delegate void InputChangedHandler(Vector2 input);

    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private float handleRange = 1f;
    [SerializeField] private float deadZone;

    private RectTransform _baseRect;
    private Canvas _canvas;
    private Camera _eventCamera;
    private Vector2 _input;
    private Vector2 _radius;
    private InputChangedHandler _onInputChanged;

    private void Awake()
    {
        _baseRect = transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        _input = Vector2.zero;

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
        x = _input.x;
        y = _input.y;
    }

    public Vector2 GetInput()
    {
        return _input;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_baseRect == null || background == null)
        {
            return;
        }

        ResolveEventCamera();

        Vector2 screenPoint = eventData.position;
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, screenPoint, _eventCamera, out localPoint))
        {
            return;
        }

        localPoint.x /= _radius.x;
        localPoint.y /= _radius.y;

        _input = localPoint;
        float magnitude = _input.magnitude;
        if (magnitude > deadZone)
        {
            if (magnitude > 1f)
            {
                _input.x /= magnitude;
                _input.y /= magnitude;
            }
        }
        else
        {
            _input.x = 0f;
            _input.y = 0f;
        }

        if (handle != null)
        {
            handle.anchoredPosition = new Vector2(
                _input.x * _radius.x * handleRange,
                _input.y * _radius.y * handleRange);
        }

        if (_onInputChanged != null)
        {
            _onInputChanged(_input);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _input.x = 0f;
        _input.y = 0f;

        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }

        if (_onInputChanged != null)
        {
            _onInputChanged(_input);
        }
    }

    private void ResolveEventCamera()
    {
        if (_canvas == null)
        {
            _eventCamera = null;
            return;
        }

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            _eventCamera = null;
            return;
        }

        _eventCamera = _canvas.worldCamera;
    }
}
