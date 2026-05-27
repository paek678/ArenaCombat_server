using System;
using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

using LobbyPlayer = Unity.Services.Lobbies.Models.Player;

namespace ArenaCombat.UI.TestUI
{
    public class LobbyPlayerSlotsUI : MonoBehaviour
    {
        [Header("=== Container ===")]
        [SerializeField] private Transform playerSlotsContainer;

        [Header("=== Slot Style ===")]
        [SerializeField] private float slotHeight = 60f;
        [SerializeField] private float slotSpacing = 10f;
        [SerializeField] private Color emptySlotColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color filledSlotColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
        [SerializeField] private Color mySlotColor = new Color(0.2f, 0.6f, 0.3f, 0.8f);
        [SerializeField] private Color hostSlotColor = new Color(0.6f, 0.5f, 0.2f, 0.8f);

        public event Action<int> OnSlotActionClicked;

        private List<PlayerSlotData> playerSlots = new List<PlayerSlotData>();

        private class PlayerSlotData
        {
            public GameObject slotObject;
            public Image background;
            public TextMeshProUGUI slotNumberText;
            public TextMeshProUGUI playerNameText;
            public TextMeshProUGUI statusText;
            public Button actionButton;
            public string playerId;
            public string playerName;
            public bool isOccupied;
            public bool isMe;
        }

        public void CreateSlots(int count)
        {
            ClearSlots();

            if (playerSlotsContainer == null)
            {
                Debug.LogError("[LobbyPlayerSlotsUI] PlayerSlotsContainer is not assigned.");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                PlayerSlotData slotData = CreateSlotUI(i + 1);
                playerSlots.Add(slotData);
            }
        }

        public void UpdateSlots(Lobby lobby, string myPlayerId)
        {
            if (lobby == null) return;

            for (int i = 0; i < playerSlots.Count; i++)
            {
                PlayerSlotData slot = playerSlots[i];

                if (i < lobby.Players.Count)
                {
                    LobbyPlayer player = lobby.Players[i];
                    string playerName = GetPlayerName(player);
                    bool isHost = player.Id == lobby.HostId;
                    bool isMe = player.Id == myPlayerId;

                    SetSlotPlayer(slot, player.Id, playerName, isHost, isMe);
                }
                else
                {
                    SetSlotEmpty(slot);
                }
            }
        }

        public void ClearSlots()
        {
            foreach (var slot in playerSlots)
            {
                if (slot.slotObject != null)
                    Destroy(slot.slotObject);
            }
            playerSlots.Clear();
        }

        private PlayerSlotData CreateSlotUI(int slotNumber)
        {
            PlayerSlotData data = new PlayerSlotData();

            GameObject slotObj = new GameObject($"PlayerSlot_{slotNumber}");
            slotObj.transform.SetParent(playerSlotsContainer, false);
            data.slotObject = slotObj;

            RectTransform slotRect = slotObj.AddComponent<RectTransform>();
            slotRect.sizeDelta = new Vector2(0, slotHeight);
            slotRect.anchorMin = new Vector2(0, 1);
            slotRect.anchorMax = new Vector2(1, 1);
            slotRect.pivot = new Vector2(0.5f, 1);

            float yPos = -(slotNumber - 1) * (slotHeight + slotSpacing);
            slotRect.anchoredPosition = new Vector2(0, yPos);

            HorizontalLayoutGroup layout = slotObj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 5, 5);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            data.background = slotObj.AddComponent<Image>();
            data.background.color = emptySlotColor;

            data.slotNumberText = CreateTextElement(slotObj.transform, $"#{slotNumber}", 50);
            data.slotNumberText.alignment = TextAlignmentOptions.Center;

            data.playerNameText = CreateTextElement(slotObj.transform, "Empty", 200);
            data.playerNameText.alignment = TextAlignmentOptions.Left;

            data.statusText = CreateTextElement(slotObj.transform, "", 80);
            data.statusText.alignment = TextAlignmentOptions.Center;
            data.statusText.fontSize = 14;

            data.actionButton = CreateButtonElement(slotObj.transform, "Action", 80);
            int capturedSlotNumber = slotNumber;
            data.actionButton.onClick.AddListener(() => OnSlotActionClicked?.Invoke(capturedSlotNumber - 1));
            data.actionButton.gameObject.SetActive(false);

            return data;
        }

        private TextMeshProUGUI CreateTextElement(Transform parent, string text, float width)
        {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(parent, false);

            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, slotHeight);

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 18;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            LayoutElement layoutElement = textObj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.flexibleWidth = 0;

            return tmp;
        }

        private Button CreateButtonElement(Transform parent, string text, float width)
        {
            GameObject buttonObj = new GameObject("ActionButton");
            buttonObj.transform.SetParent(parent, false);

            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, slotHeight - 10);

            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.4f, 0.4f, 0.4f, 1f);

            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            colors.pressedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            button.colors = colors;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            LayoutElement layoutElement = buttonObj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.flexibleWidth = 0;

            return button;
        }

        private void SetSlotPlayer(PlayerSlotData slot, string playerId, string playerName, bool isHost, bool isMe)
        {
            slot.playerId = playerId;
            slot.playerName = playerName;
            slot.isOccupied = true;
            slot.isMe = isMe;

            string hostMark = isHost ? " [HOST]" : "";
            string meMark = isMe ? " (Me)" : "";
            slot.playerNameText.text = $"{playerName}{hostMark}{meMark}";

            slot.statusText.text = "Online";
            slot.statusText.color = Color.green;

            slot.actionButton.gameObject.SetActive(false);

            if (isMe)
                slot.background.color = mySlotColor;
            else if (isHost)
                slot.background.color = hostSlotColor;
            else
                slot.background.color = filledSlotColor;
        }

        private void SetSlotEmpty(PlayerSlotData slot)
        {
            slot.playerId = null;
            slot.playerName = null;
            slot.isOccupied = false;
            slot.isMe = false;

            slot.playerNameText.text = "Empty";
            slot.statusText.text = "";
            slot.actionButton.gameObject.SetActive(false);
            slot.background.color = emptySlotColor;
        }

        private string GetPlayerName(LobbyPlayer player)
        {
            if (player.Data != null && player.Data.TryGetValue("PlayerName", out var nameData))
                return nameData.Value;
            return $"Player_{player.Id.Substring(0, 6)}";
        }
    }
}
