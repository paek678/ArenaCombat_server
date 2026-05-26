using UnityEngine;

[CreateAssetMenu(fileName = "AbilityCard", menuName = "Cards/AbilityCard")]
public class AbilityCard : ScriptableObject
{
    public string cardName;
    public Sprite cardIcon;
    public string description;
}