using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace IRSequencer.Gui
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class UIAssetsLoader : MonoBehaviour
    {
        private AssetBundle IRAssetBundle;

        internal static GameObject controlWindowPrefab;
        internal static GameObject sequencerLinePrefab;
        internal static GameObject stateLinePrefab;
        internal static GameObject sequenceLinePrefab;

        internal static GameObject uiSettingsWindowPrefab;

        internal static GameObject editorWindowPrefab;
        internal static GameObject sequenceCommandLinePrefab;
        
        internal static GameObject basicTooltipPrefab;

        internal static List<Texture2D> iconAssets;
        internal static List<UnityEngine.Sprite> spriteAssets;
        
        public static bool allPrefabsReady = false;
        
        public IEnumerator LoadBundle(string location)
        {
            while (!Caching.ready)
                yield return null;
            using (WWW www = WWW.LoadFromCacheOrDownload(location, 1))
            {
                yield return www;
                IRAssetBundle = www.assetBundle;

                LoadBundleAssets();

                IRAssetBundle.Unload(false);
            }
        }
        
        private void LoadBundleAssets()
        {
            var prefabs = IRAssetBundle.LoadAllAssets<GameObject>();
            int prefabsLoadedCount = 0;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i].name == "SequencerMainWindowPrefab")
                {
                    controlWindowPrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }
                if (prefabs[i].name == "SequencerLinePrefab")
                {
                    sequencerLinePrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }

                if (prefabs[i].name == "SequencerStateLinePrefab")
                {
                    stateLinePrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }

                if (prefabs[i].name == "SequenceLinePrefab")
                {
                    sequenceLinePrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }

                if (prefabs[i].name == "UISettingsWindowPrefab")
                {
                    uiSettingsWindowPrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }

                if (prefabs[i].name == "SequencerEditorWindowPrefab")
                {
                    editorWindowPrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }

                if (prefabs[i].name == "SequenceCommandLine")
                {
                    sequenceCommandLinePrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }

                if (prefabs[i].name == "BasicTooltipPrefab")
                {
                    basicTooltipPrefab = prefabs[i] as GameObject;
                    prefabsLoadedCount++;
                }
            }

            allPrefabsReady = (prefabsLoadedCount > 7);

            spriteAssets = new List<UnityEngine.Sprite>();
            var sprites = IRAssetBundle.LoadAllAssets<UnityEngine.Sprite>();

            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                {
                    spriteAssets.Add(sprites[i]);
                }
            }

            iconAssets = new List<Texture2D>();
            var icons = IRAssetBundle.LoadAllAssets<Texture2D>();

            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] != null)
                {
                    iconAssets.Add(icons[i]);
                }
            }

            if(allPrefabsReady)
                Logger.Log("Successfully loaded all prefabs from AssetBundle");
            else
                Logger.Log("Some prefabs failed to load, bundle = " + IRAssetBundle.name);
        }

        public void LoadBundleFromDisk(string path)
        {
            IRAssetBundle = AssetBundle.CreateFromFile(path);

            LoadBundleAssets();

            //had to move bundle unloading further down in time due to unexplained and unreproducable on my PC issues for some users
        }

        public void Start()
        {
            var assemblyFile = Assembly.GetExecutingAssembly().Location;
            //we will use same path for AssetBundles as IR and share some assets
            //var bundlePath = "file://" + assemblyFile.Replace(new FileInfo(assemblyFile).Name, "").Replace("\\","/") + "../../AssetBundles/";
            var filePath = assemblyFile.Replace(new FileInfo(assemblyFile).Name, "") + "../../AssetBundles/";

            //Logger.Log("Loading bundles from BundlePath: " + bundlePath, Logger.Level.Debug);

            //need to clean cache
            //Caching.CleanCache();

            //StartCoroutine(LoadBundle(bundlePath + "ir_ui_objects.ksp"));

            Logger.Log("Loading bundles from filePath: " + filePath, Logger.Level.Debug);
            LoadBundleFromDisk(filePath + "ir_ui_objects.ksp");
        }

        public void OnDestroy()
        {
            Logger.Log("Unloading bundle", Logger.Level.Debug);
            IRAssetBundle.Unload(false);
        }
    }
}

