using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace IRSequencer.Utility
{
    public class TextureLoader
    {
        private static bool isReady;
        
        protected static TextureLoader LoaderInstance;

        public static TextureLoader Instance
        {
            get { return LoaderInstance; }
        }

        public static bool Ready { get { return isReady; } }

        /// <summary>
        ///     Load the textures from files to memory
        /// </summary>
        
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
                        //Logger.Log(string.Format("[GUI] Loading: {0}/{1}", pathPluginTextures, fileName));
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
