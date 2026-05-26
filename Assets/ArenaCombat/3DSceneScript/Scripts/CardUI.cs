using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

public class CardUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image icon;
    private AbilityCard cardData;
    private CardManager cardManager;

    private Material cardMaterial;
    private Vector3 originalScale;
    private bool isSelected;
    private bool pendingAppearAnimation;

    public void Setup(AbilityCard card, CardManager manager)
    {
        if (card == null || icon == null)
        {
            return;
        }

        cardData = card;
        cardManager = manager;
        icon.sprite = card.cardIcon;
        originalScale = transform.localScale;

        // Recreate instance material each setup so dissolve anim is isolated per slot.
        if (cardMaterial != null)
        {
            Destroy(cardMaterial);
        }

        cardMaterial = Instantiate(icon.material);
        icon.material = cardMaterial;

        if (card.cardIcon != null && card.cardIcon.texture != null)
        {
            cardMaterial.SetTexture("_MainTex", card.cardIcon.texture);
        }

        cardMaterial.SetFloat("_Dissolve", 1.0f);

        isSelected = false;

        // If this object is inactive, defer coroutine until OnEnable.
        if (isActiveAndEnabled && gameObject.activeInHierarchy)
        {
            pendingAppearAnimation = false;
            StartCoroutine(PlayAppearAnimation());
        }
        else
        {
            pendingAppearAnimation = true;
        }
    }

    private void OnEnable()
    {
        if (!pendingAppearAnimation)
        {
            return;
        }

        pendingAppearAnimation = false;
        StartCoroutine(PlayAppearAnimation());
    }

    private void OnDisable()
    {
        // Ensure stale running animations do not continue while disabled.
        StopAllCoroutines();
    }

    private void OnDestroy()
    {
        if (cardMaterial != null)
        {
            Destroy(cardMaterial);
        }
    }

    private IEnumerator PlayAppearAnimation()
    {
        if (cardMaterial == null)
        {
            yield break;
        }

        float duration = 1.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            cardMaterial.SetFloat("_Dissolve", Mathf.Lerp(1.0f, 0f, t));
            yield return null;
        }

        cardMaterial.SetFloat("_Dissolve", 0f);
    }

    public IEnumerator PlayDisappearAnimation()
    {
        if (cardMaterial == null)
        {
            yield break;
        }

        float duration = 1.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            cardMaterial.SetFloat("_Dissolve", Mathf.Lerp(0f, 1f, t));
            yield return null;
        }

        cardMaterial.SetFloat("_Dissolve", 1f);
    }

    public void OnClick()
    {
        if (cardData == null || isSelected || cardManager == null)
        {
            return;
        }

        isSelected = true;
        cardManager.OnCardSelected(cardData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isSelected)
        {
            transform.localScale = originalScale * 1.05f;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = originalScale;
    }
}
