using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace IRSequencer.Gui
{
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    public class UIAssetsLoader : MonoBehaviour
    {
        private AssetBundle IRAssetBundle;

        internal static GameObject controlWindowPrefab;
        internal static GameObject sequencerLinePrefab;
        internal static GameObject stateLinePrefab;
        internal static GameObject sequenceLinePrefab;

        internal static GameObject uiSettingsWindowPrefab;

        internal static GameObject editorWindowPrefab;
        
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
                var prefabs = IRAssetBundle.LoadAllAssets<GameObject>();
                int prefabsLoadedCount = 0;
                for (int i=0; i< prefabs.Length; i++)
                {
                    if(prefabs[i].name == "SequencerMainWindowPrefab")
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

                    if (prefabs[i].name == "EditorWindowPrefab")
                    {
                        editorWindowPrefab = prefabs[i] as GameObject;
                        prefabsLoadedCount++;
                    }

                    if (prefabs[i].name == "BasicTooltipPrefab")
                    {
                        basicTooltipPrefab = prefabs[i] as GameObject;
                        prefabsLoadedCount++;
                    }
                }

                allPrefabsReady = (prefabsLoadedCount >= 7);

                spriteAssets = new List<UnityEngine.Sprite>();
                var sprites = IRAssetBundle.LoadAllAssets<UnityEngine.Sprite>();

                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] != null)
                    {
                        spriteAssets.Add(sprites[i]);
                        Logger.Log("Successfully loaded Sprite " + sprites[i].name);
                    }
                }

                iconAssets = new List<Texture2D>();
                var icons = IRAssetBundle.LoadAllAssets<Texture2D>();

                for (int i = 0; i < icons.Length; i++)
                {
                    if (icons[i] != null)
                    {
                        iconAssets.Add(icons[i]);
                        Logger.Log("Successfully loaded texture " + icons[i].name);
                    }
                    
                }

                IRAssetBundle.Unload(false);
            }
        }
        
        public void Start()
        {
            var assemblyFile = Assembly.GetExecutingAssembly().Location;
            //we will use same path for AssetBundles as IR and share some assets
            var bundlePath = "file://" + assemblyFile.Replace(new FileInfo(assemblyFile).Name, "").Replace("\\","/") + "../../AssetBundles/";

            Logger.Log("Loading bundles from BundlePath: " + bundlePath);

            //need to clean cache
           // Caching.CleanCache();
            
            StartCoroutine(LoadBundle(bundlePath + "ir_ui_objects.ksp"));
           
        }
    }
}

