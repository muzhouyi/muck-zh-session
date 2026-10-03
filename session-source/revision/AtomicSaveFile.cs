namespace MuckSaveGame
{
    using System;
    using System.IO;
    public static class AtomicSaveFile
    {
        public static void Write(string path, Action<Stream> write)
        {
            string pending = path + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write))
                {
                    write(stream);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(pending, path, path + ".bak");
                else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
