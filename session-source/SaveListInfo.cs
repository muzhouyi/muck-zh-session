namespace MuckSaveGame
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Xml;
    using System.Xml.Linq;
    public static class SaveListInfo
    {
        public static string Describe(string path)
        {
            string time;
            try { time = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"); } catch { time = "未知"; }
            string days = "天数未知";
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64000000 };
                using (var reader = XmlReader.Create(path, settings))
                {
                    var xml = XDocument.Load(reader);
                    var day = xml.Descendants("WorldData").Elements("CurrentDay").FirstOrDefault();
                    if (day == null) day = xml.Descendants("currentDay").FirstOrDefault();
                    if (day != null && int.TryParse(day.Value, out int count) && count >= 0) days = "存活 " + count + " 天";
                }
            }
            catch { days = "无法读取天数"; }
            return Path.GetFileNameWithoutExtension(path) + "\n修改 " + time + "  ·  " + days;
        }
    }
}
