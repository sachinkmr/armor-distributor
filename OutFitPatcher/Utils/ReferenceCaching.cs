using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace OutFitPatcher.Utils
{
    public class ReferenceCaching {
        private static readonly string CacheDirPath = Environment.SpecialFolder.LocalApplicationData.ToString();
        private const string CacheExtension = ".mutagenRefCache";
        private const string TempCacheExtension = ".mutagenRefCacheTemp";
        private static HashAlgorithm Algorithm = SHA256.Create();
        public static Dictionary<FormKey, TreeNode<FormKey>> References = new();


        /** Builds Reference cache of one mod */
        public static void BuildReferenceCache(IModGetter mod) {
            var refCache = new Dictionary<FormKey, List<FormKey>>();

            //Fill refCache
            bool HasNext = false;
            IEnumerator<IMajorRecordCommonGetter> itr = mod.EnumerateMajorRecords().GetEnumerator();
            do
            {
                try
                {
                    HasNext = itr.MoveNext();
                }
                catch (Exception)
                {
                    HasNext = itr.MoveNext();
                }

                if (HasNext) {
                    var record = itr.Current;
                    if (record != null && !record.IsDeleted)
                    {
                        foreach (var formLinkGetter in record.ContainedFormLinks.Where(x=>x!=null))
                        {
                            if (References.ContainsKey(formLinkGetter.FormKey))
                            {
                                refCache.TryGetValue(formLinkGetter.FormKey, out var references);
                                references?.Add(record.FormKey);
                            }
                            else
                            {
                                refCache.Add(formLinkGetter.FormKey, new List<FormKey> { record.FormKey });
                            }
                        }
                    }
                }
            } while (HasNext);

            //Write refCache to file
            Directory.CreateDirectory(CacheDirPath);
            var name = GetHashString(mod.ModKey.Name);
            var file = Path.Combine(Environment.CurrentDirectory, CacheDirPath, name+CacheExtension);
            using (var fileStream = File.OpenWrite(file)) {
                BinaryWriter writer;
                using (var zip = new GZipStream(fileStream, CompressionMode.Compress)) {
                    writer = new BinaryWriter(zip);
                    writer.Write(DateTime.Now.ToString(CultureInfo.InvariantCulture));
                    writer.Write(refCache.Count);
                    foreach (var (key, value) in refCache) {
                        writer.Write(key.ToString());
                        writer.Write(value.Count);
                        foreach (var majorRecordCommonGetter in value) {
                            writer.Write(majorRecordCommonGetter.ToString());
                        }
                    }
                }
            }
        }

        /** Regenerate cache of mod if necessary */
        private static void TryRegenerateCache(IModGetter mod) {
            var name = GetHashString(mod.ModKey.Name);
            var file = Path.Combine(Environment.CurrentDirectory, CacheDirPath, name+CacheExtension);
            if (File.Exists(file)) {
                //TODO check if mod was updated since last cache build - last cache build time is saved in first string
                // using (var br = new BinaryReader(File.OpenRead($"{CacheDirPath}\\{mod.ModKey.Name}{CacheExtension}"))) {
                //     var dateTime = DateTime.Parse(br.ReadString());
                //     if (dateTime.CompareTo(mod.) > 0) {
                //         return;
                //     }
                // }

                return;
            }

            BuildReferenceCache(mod);
        }

        /** Returns references of one form key in a mod */
        public static IEnumerable<FormKey> GetReferences(FormKey formKey, IModGetter mod) {
            //Regenerate cache if needed
            TryRegenerateCache(mod);
            
            //Create temporary uncompressed cache file
            var refs = new List<FormKey>();
            var name = GetHashString(mod.ModKey.Name);
            var file = Path.Combine(Environment.CurrentDirectory, CacheDirPath, name+CacheExtension);
            var tempFile = Path.Combine(Environment.CurrentDirectory, CacheDirPath, name+TempCacheExtension);
            using (var fileStream = File.OpenRead(file)) {
                using (var zip = new GZipStream(fileStream, CompressionMode.Decompress)) {
                    using (var tempFileStream = File.OpenWrite(tempFile)) {
                        zip.CopyTo(tempFileStream);
                    }
                }
            }

            //Search for reference in cache
            using (var reader = new BinaryReader(File.OpenRead(tempFile))) {
                reader.ReadString(); //Skip date
                var formCount = reader.ReadInt32();
                for (var i = 0; i < formCount; i++) {
                    var key = FormKey.Factory(reader.ReadString());
                    var referenceCount = reader.ReadInt32();

                    if (key != formKey) {
                        //Skip to next form
                        for (var j = 0; j < referenceCount; j++) {
                            reader.ReadString();
                        }
                    } else {
                        //Collect references and stop
                        for (var j = 0; j < referenceCount; j++) {
                            refs.Add(FormKey.Factory(reader.ReadString()));
                        }

                        break;
                    }
                }
            }
            
            //Delete temporary cache file
            File.Delete(tempFile);
            return refs;
        }

        /** Load reference cache of all saved ref caches */
        public static Dictionary<FormKey, List<FormKey>> LoadReferenceCache() {
            var refCache = new Dictionary<FormKey, List<FormKey>>();

            //Iterate all cache files
            foreach (var filePath in Directory.GetFiles(CacheDirPath)
                .Where(f => f.EndsWith(CacheExtension))) {
                var newRefCache = LoadReferenceCache(filePath);
                
                //Integrate new references from mod into combined references directory
                foreach (var (key, formKeys) in newRefCache) {
                    if (refCache.ContainsKey(key)) {
                        refCache.TryGetValue(key, out var value);
                        value?.AddRange(formKeys);
                    } else {
                        refCache.Add(key, formKeys);
                    }
                }
            }

            return refCache;
        }


        public static Dictionary<FormKey, List<FormKey>> LoadReferenceCache(String file)
        {
            var refCache = new Dictionary<FormKey, List<FormKey>>();
            using (var br = new BinaryReader(File.OpenRead(file)))
            {
                br.ReadString(); //Skip date

                //Build ref cache
                var formCount = br.ReadInt32();
                for (var i = 0; i < formCount; i++)
                {
                    var key = FormKey.Factory(br.ReadString());
                    var referenceCount = br.ReadInt32();
                    var value = new List<FormKey>();
                    for (var j = 0; j < referenceCount; j++)
                    {
                        value.Add(FormKey.Factory(br.ReadString()));
                    }

                    refCache.Add(key, value);
                }
            }

            return refCache;
        }

        /** Load ref cache of one specific mod*/
        public static Dictionary<FormKey, List<FormKey>> LoadReferenceCache(ModKey modKey) {
            var refCache = new Dictionary<FormKey, List<FormKey>>();

            //Read mod cache file
            var file = Path.Combine(Environment.CurrentDirectory, CacheDirPath, GetHashString(modKey.Name) + CacheExtension);
            using (var br = new BinaryReader(File.OpenRead(file))) {
                br.ReadString(); //Skip date

                //Build ref cache
                var formCount = br.ReadInt32();
                for (var i = 0; i < formCount; i++) {
                    var key = FormKey.Factory(br.ReadString());
                    var referenceCount = br.ReadInt32();
                    var value = new List<FormKey>();
                    for (var j = 0; j < referenceCount; j++) {
                        value.Add(FormKey.Factory(br.ReadString()));
                    }

                    refCache.Add(key, value);
                }
            }

            return refCache;
        }

        public static string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in Algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString)))
                sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}