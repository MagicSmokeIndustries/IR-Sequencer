﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace IRSequencer.Gui
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
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

                if (prefabs[i].name == "SequencerUISettingsWindowPrefab")
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
            /*
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
            */
            if(allPrefabsReady)
                Logger.Log("Successfully loaded all prefabs from AssetBundle");
            else
                Logger.Log("Some prefabs failed to load, bundle = " + IRAssetBundle.name);
        }

        public void Start()
        {
            if (allPrefabsReady)
                return; //there is no need to reload the prefabs or any other assets.
            
            //need to clean cache
            Caching.CleanCache();
            
            Type IRAssetsLoaderType = null;

            AssemblyLoader.loadedAssemblies.TypeOperation (t => {
                if (t.FullName == "InfernalRobotics.Gui.UIAssetsLoader") {
                    IRAssetsLoaderType = t;
                }
            });

            var fieldInfo = IRAssetsLoaderType.GetField("IRAssetBundle", BindingFlags.NonPublic | BindingFlags.Static);
            IRAssetBundle = (AssetBundle)fieldInfo.GetValue(null);

            if (!allPrefabsReady)
            {
                if (IRAssetBundle != null)
                {
                    LoadBundleAssets();
                }
            }

            fieldInfo = IRAssetsLoaderType.GetField("iconAssets", BindingFlags.NonPublic | BindingFlags.Static);
            iconAssets = (List<Texture2D>)fieldInfo.GetValue(null);

            fieldInfo = IRAssetsLoaderType.GetField("spriteAssets", BindingFlags.NonPublic | BindingFlags.Static);
            spriteAssets = (List<Sprite>)fieldInfo.GetValue(null);

        }

        public void Update()
        {
            if(!allPrefabsReady)
            {
                Type IRAssetsLoaderType = null;

                AssemblyLoader.loadedAssemblies.TypeOperation(t =>
                {
                    if (t.FullName == "InfernalRobotics.Gui.UIAssetsLoader")
                    {
                        IRAssetsLoaderType = t;
                    }
                });

                var fieldInfo = IRAssetsLoaderType.GetField("IRAssetBundle", BindingFlags.NonPublic | BindingFlags.Static);
                IRAssetBundle = (AssetBundle)fieldInfo.GetValue(null);

                if (IRAssetBundle != null)
                {
                    LoadBundleAssets();
                }
                
                fieldInfo = IRAssetsLoaderType.GetField("iconAssets", BindingFlags.NonPublic | BindingFlags.Static);
                iconAssets = (List<Texture2D>)fieldInfo.GetValue(null);

                fieldInfo = IRAssetsLoaderType.GetField("spriteAssets", BindingFlags.NonPublic | BindingFlags.Static);
                spriteAssets = (List<Sprite>)fieldInfo.GetValue(null);
            }
        }

        public void OnDestroy()
        {
            /*if(IRAssetBundle)
            {
                Logger.Log("Unloading bundle", Logger.Level.Debug);
                IRAssetBundle.Unload(false);
            }*/
        }
    }
}

