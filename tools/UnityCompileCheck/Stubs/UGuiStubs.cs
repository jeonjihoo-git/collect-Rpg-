// uGUI(com.unity.ugui) 공개 API 시그니처 스텁 — 컴파일 검사 전용(빌드/런타임에 포함되지 않음).
// Unity 에서는 실제 패키지가 사용된다. 이 저장소 코드가 쓰는 멤버만 실제 이름·형태대로 선언한다.
#pragma warning disable CS0067, CS0108, CS0114
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.EventSystems
{
    public abstract class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { public static EventSystem current { get; set; } }
    public abstract class BaseInputModule : UIBehaviour { }
    public abstract class PointerInputModule : BaseInputModule { }
    public class StandaloneInputModule : PointerInputModule { }
    public abstract class BaseRaycaster : UIBehaviour { }
    public class BaseEventData { }
    public class PointerEventData : BaseEventData
    {
        public int pointerId { get; set; }
        public Vector2 position { get; set; }
        public Vector2 delta { get; set; }
        public Vector2 pressPosition { get; set; }
        public Camera pressEventCamera { get; }
        public Camera enterEventCamera { get; }
    }
    public interface IEventSystemHandler { }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData eventData); }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData eventData); }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;

    public abstract class Graphic : UIBehaviour
    {
        public virtual Color color { get; set; }
        public virtual bool raycastTarget { get; set; }
        public RectTransform rectTransform { get; }
        public Canvas canvas { get; }
        public virtual Material material { get; set; }
    }

    public abstract class MaskableGraphic : Graphic { }

    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public Sprite sprite { get; set; }
        public Type type { get; set; }
        public float fillAmount { get; set; }
        public bool preserveAspect { get; set; }
    }

    public class Text : MaskableGraphic
    {
        public virtual string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool supportRichText { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public float lineSpacing { get; set; }
    }

    public struct ColorBlock
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
        public Color selectedColor { get; set; }
        public Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }
    }

    public class Selectable : UIBehaviour
    {
        public bool interactable { get; set; }
        public Graphic targetGraphic { get; set; }
        public ColorBlock colors { get; set; }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; }
    }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
    }

    public class GraphicRaycaster : BaseRaycaster { }

    public abstract class BaseMeshEffect : UIBehaviour { }

    public class Shadow : BaseMeshEffect
    {
        public Color effectColor { get; set; }
        public Vector2 effectDistance { get; set; }
    }

    public class Outline : Shadow { }
}
