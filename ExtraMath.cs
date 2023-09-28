using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;

namespace ModCheck
{
    public static class ExtraMath
    {
        static SHA256 twoFiftySixHasher = SHA256.Create();
        static SHA256 tfs => twoFiftySixHasher;

        public static string Sha256HashMod(Mod mod)
        {
            return mod.SourceType == EnumModSourceType.Folder ? Sha256HashFolder(mod.SourcePath) : Sha256HashFile(mod.SourcePath);
        }

        public static string Sha256HashFile(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            {
                string str = GetString(tfs.ComputeHash(stream));
                return str;
            }
        }

        public static string Sha256HashFolder(string folderPath)
        {
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
            Array.Sort(files);
            byte[] hash = new byte[0];

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];

                string relPath = file.Substring(folderPath.Length + 1);
                byte[] pathBytes = Encoding.UTF8.GetBytes(relPath.ToLowerInvariant());

                tfs.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

                byte[] contentBytes = File.ReadAllBytes(file);

                if (i < files.Length)
                {
                    tfs.TransformBlock(contentBytes, 0, contentBytes.Length, contentBytes, 0);
                }
                else
                {
                    hash = tfs.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
                }
            };
            return GetString(hash);
        }

        public static string GetString(byte[] hash)
        {
            string hashString = "";
            for (int i = 0; i < hash.Length; i++)
            {
                hashString += hash[i].ToString("x2");
            }
            return hashString;

        }

        public static string Sha256Hash(string value)
        {
            var hash = tfs.ComputeHash(Encoding.UTF8.GetBytes(value));
            return GetString(hash);
        }
    }
}
