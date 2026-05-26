using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class IconHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Sprite cardSprite;
    private Image bigPreviewImage;

    public void Setup(Sprite sprite, Image previewImage)
    {
        cardSprite = sprite;
        bigPreviewImage = previewImage;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (bigPreviewImage != null)
        {
            bigPreviewImage.sprite = cardSprite;
            bigPreviewImage.gameObject.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (bigPreviewImage != null)
        {
            bigPreviewImage.gameObject.SetActive(false);
        }
    }
}