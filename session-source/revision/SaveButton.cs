namespace MuckSaveGame
{
	using TMPro;
	using UnityEngine;
	using UnityEngine.EventSystems;

	public class SaveButton : MonoBehaviour, IPointerClickHandler
	{
		public string FileName = "";
		void Start()
		{
			if (FileName.Length == 0) FileName = GetComponentInChildren<TextMeshProUGUI>().text; UIManager.saveButtons[FileName] = this;
		}
		public void OnPointerClick(PointerEventData eventData)
		{
			if (eventData.button == PointerEventData.InputButton.Left)
			{
				LoadManager.selectedSavePath = SaveSystem.GetPathForFileName(FileName);
				UIManager.selectionGUI?.SetActive(false);
			}
			else if (eventData.button == PointerEventData.InputButton.Right)
			{
				if (UIManager.buttonExtraGUI != null)
				{
					UIManager.buttonExtraGUI.transform.position = new Vector3(eventData.position.x + 200, eventData.position.y - 125);
					UIManager.buttonExtraGUI.SetActive(true);
				}

				UIManager.curEditSave = FileName;

				if (UIManager.buttonExtraGUI != null)
				{
					UIManager.buttonExtraGUI.GetComponentInChildren<TextMeshProUGUI>().text = UIManager.curEditSave.Length > 12
						? FileName.Substring(0, 12) + "..."
						: FileName;
				}
			}
		}
		public void UpdateText(string text)
		{
			FileName = text; GetComponentInChildren<TextMeshProUGUI>().text = SaveListInfo.Describe(SaveSystem.GetPathForFileName(text));
		}
	}
}
