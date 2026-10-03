namespace MuckSaveGame
{
	using System;
	using System.Collections;
	using System.Reflection;
	using UnityEngine;

	public class WorldTimer : MonoBehaviour
	{
		private IEnumerator? coroutine;
		public void StartSave(float _time)
		{
			coroutine = SaveCoroutine(_time);
			StartCoroutine(coroutine);
		}
		public void StartPlayerDeath(float _time)
		{
			Plugin.Log.LogInfo("Starting Player Death");
			coroutine = PlayerDeathCoroutine(_time);
			Plugin.Log.LogInfo("Player Death Started");
			StartCoroutine(coroutine);
		}
		private IEnumerator PlayerDeathCoroutine(float _time)
		{
			Plugin.Log.LogInfo("Player Death Started Coroutine");
			yield return new WaitForSecondsRealtime(_time);

			var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

			Plugin.Log.LogInfo("PLAYER DYING");

			typeof(PlayerStatus).GetMethod("PlayerDied", flags).Invoke(PlayerStatus.Instance, new object[] { 0, -1 });
			Plugin.Log.LogInfo("Player Died");
			UnityEngine.Object.Destroy(this);
		}
        private IEnumerator SaveCoroutine(float delay)
        {
            float deadline = Time.realtimeSinceStartup + Math.Max(15f, delay);
            while (!SessionControl.Gate.Complete && Time.realtimeSinceStartup < deadline)
                yield return null;
            try
            {
                if (!SessionControl.Gate.Complete)
                    throw new InvalidOperationException("队友数据未收齐。旧存档已保留，请确认所有人都安装了同版本存档模组。");
                if (World.isLeavingIsland || string.IsNullOrEmpty(LoadManager.selectedSavePath))
                    throw new InvalidOperationException("当前状态无法存档。");
                SaveSystem.Save(LoadManager.selectedSavePath);
                SessionControl.Say("存档完成！可以退出，稍后从大厅读取存档继续。");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(ex.ToString());
                SessionControl.Say("存档失败：" + ex.Message);
                UIManager.canSaveAfter = DateTime.MinValue;
            }
            finally { SessionControl.EndSave(); UnityEngine.Object.Destroy(this); }
        }
    }
}
