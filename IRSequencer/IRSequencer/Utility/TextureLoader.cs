using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace IRSequencer.Utility
{
    public class TextureLoader
    {
        private static bool isReady;
        internal static Texture2D EditorBackgroundText { get; private set; }
        //internal static Texture2D Transparent { get; private set; }
        internal static Texture2D PlayheadBG { get; private set; }
        internal static Texture2D PlayheadBGPaused { get; private set; }

        internal static Texture2D ToggleBG { get; private set; }
        internal static Texture2D ToggleBGHover { get; private set; }

        internal static Texture2D ExpandIcon { get; private set; }
        internal static Texture2D CollapseIcon { get; private set; }
        internal static Texture2D DownIcon { get; private set; }
        internal static Texture2D UpIcon { get; private set; }
        internal static Texture2D TrashIcon { get; private set; }
        internal static Texture2D PlayIcon { get; private set; }
        internal static Texture2D PauseIcon { get; private set; }
        internal static Texture2D StopIcon { get; private set; }
        internal static Texture2D EditIcon { get; private set; }
        internal static Texture2D CloneIcon { get; private set; }
        internal static Texture2D LoopIcon { get; private set; }
        internal static Texture2D LoopingIcon { get; private set; }
        internal static Texture2D AutoStartIcon { get; private set; }

        internal static Texture2D LockedIcon { get; private set; }
        internal static Texture2D UnlockedIcon { get; private set; }

        internal static Texture2D DisabledPlayIcon { get; private set; }
        internal static Texture2D DisabledStopIcon { get; private set; }

        internal static Texture2D BgIcon { get; private set; }

        protected static TextureLoader LoaderInstance;

        public static TextureLoader Instance
        {
            get { return LoaderInstance; }
        }

        public static bool Ready { get { return isReady; } }

        /// <summary>
        ///     Load the textures from files to memory
        /// </summary>
        public static void InitTextures()
        {
            if (!isReady)
            {
                //const string texPath = "MagicSmokeIndustries/Textures/";
                EditorBackgroundText = CreateTextureFromColor(1, 1, new Color32(81, 86, 94, 255));
                PlayheadBG = CreateTextureFromColor(1, 1, new Color32(85, 170, 0, 64));
                PlayheadBGPaused = CreateTextureFromColor(1, 1, new Color32(255, 170, 0, 64));

                ToggleBG = CreateBorderTextureFromColor(25, 25, new Color32(128, 128, 128, 64), new Color32(155, 155, 155, 255));
                ToggleBGHover = CreateBorderTextureFromColor(25, 25, new Color32(200, 200, 200, 64), Color.white);
                //Transparent = CreateTextureFromColor(1, 1, new Color32(255, 255, 255, 0));

                //ExpandIcon = ToggleBG;
                //CollapseIcon = ToggleBGHover;
                //ExpandIcon = GameDatabase.Instance.GetTexture(texPath + "expand.png", false);
                ExpandIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(ExpandIcon, "expand.png");

                CollapseIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(CollapseIcon, "collapse.png");

                DownIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(DownIcon, "down.png");

                UpIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(UpIcon, "up.png");

                TrashIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(TrashIcon, "trash.png");

                BgIcon = new Texture2D(9, 9, TextureFormat.ARGB32, false);
                LoadImageFromFile(BgIcon, "icon_background.png");

                PlayIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(PlayIcon, "play.png");

                PauseIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(PauseIcon, "pause.png");

                StopIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(StopIcon, "stop.png");

                EditIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(EditIcon, "edit.png");

                CloneIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(CloneIcon, "clone.png");

                LoopIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(LoopIcon, "loop.png");

                LoopingIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(LoopingIcon, "looping.png");

                AutoStartIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(AutoStartIcon, "auto_start.png");

                DisabledPlayIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(DisabledPlayIcon, "disabled_play.png");

                DisabledStopIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(DisabledStopIcon, "disabled_stop.png");

                LockedIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(LockedIcon, "locked.png");

                UnlockedIcon = new Texture2D(32, 32, TextureFormat.ARGB32, false);
                LoadImageFromFile(UnlockedIcon, "unlocked.png");

                isReady = true;
            }
        }

        /// <summary>
        ///     Use System.IO.File to read a file into a texture in RAM. Path is relative to the DLL
        ///     Do it this way so the images are not affected by compression artifacts or Texture quality settings
        /// </summary>
        /// <param name="tex">Texture to load</param>
        /// <param name="fileName">Filename of the image in side the Textures folder</param>
        /// <returns></returns>
        internal static bool LoadImageFromFile(Texture2D tex, string fileName)
        {
            //Set the Path variables
            string pluginPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string pathPluginTextures = string.Format("{0}/../Textures", pluginPath);
            bool blnReturn = false;
            try
            {
                //File Exists check
                if (File.Exists(string.Format("{0}/{1}", pathPluginTextures, fileName)))
                {
                    try
                    {
                        Logger.Log(string.Format("[GUI] Loading: {0}/{1}", pathPluginTextures, fileName));
                        tex.LoadImage(File.ReadAllBytes(string.Format("{0}/{1}", pathPluginTextures, fileName)));
                        blnReturn = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(string.Format("[GUI] Failed to load the texture:{0} ({1})",
                            string.Format("{0}/{1}", pathPluginTextures, fileName), ex.Message));
                    }
                }
                else
                {
                    Logger.Log(string.Format("[GUI] Cannot find texture to load:{0}",
                        string.Format("{0}/{1}", pathPluginTextures, fileName)));
                }
            }
            catch (Exception ex)
            {
                Logger.Log(string.Format("[GUI] Failed to load (are you missing a file):{0} ({1})",
                    string.Format("{0}/{1}", pathPluginTextures, fileName), ex.Message));
            }
            return blnReturn;
        }

        /// <summary>
        /// Creates the solid texture of given size and Color.
        /// </summary>
        /// <returns>The texture from color.</returns>
        /// <param name="width">Width</param>
        /// <param name="height">Height</param>
        /// <param name="col">Color</param>
        private static Texture2D CreateTextureFromColor(int width, int height, Color col)
        {
            var pix = new Color[width * height];

            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();

            return result;
        }

        private static Texture2D CreateBorderTextureFromColor(int width, int height, Color col, Color borderCol)
        {

            var result = CreateTextureFromColor (width, height, col);

            for (int x = 0; x < result.width; x++) 
            {
                for (int y = 0; y < result.height; y++) 
                {
                    if (x < 1 || x>result.width-2) 
                        result.SetPixel(x, y, borderCol);
                    else if (y < 1 || y>result.height-2) 
                        result.SetPixel(x, y, borderCol);
                }
            }

            result.Apply();

            return result;
        }

    }
}
