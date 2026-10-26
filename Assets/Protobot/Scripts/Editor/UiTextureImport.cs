using System.IO;
using UnityEditor;
using UnityEngine;

namespace Protobot.Editor
{
    /// <summary>
    /// Import settings for the generated UI art (Tools/Textures/make_cursors.py, make_ui_sprites.py):
    /// everything is an uncompressed sprite; cursor icons stay readable so they can also be the OS cursor in menus,
    /// and the panel shapes get their 9-slice borders.
    /// </summary>
    public class UiTextureImport : AssetPostprocessor
    {
        public const string CursorFolder = "Assets/Protobot/Art/UI/Cursors/";
        public const string SpriteFolder = "Assets/Protobot/Art/UI/Sprites/";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(PartPrefabBuilder.ThumbnailFolder))
            {
                var thumb = (TextureImporter)assetImporter;
                thumb.textureType = TextureImporterType.Sprite;
                thumb.spriteImportMode = SpriteImportMode.Single;
                thumb.mipmapEnabled = false;
                thumb.maxTextureSize = 256;
                thumb.textureCompression = TextureImporterCompression.CompressedHQ;
                return;
            }

            var isCursor = assetPath.StartsWith(CursorFolder);
            if (!isCursor && !assetPath.StartsWith(SpriteFolder))
                return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = isCursor;  // Cursor.SetCursor needs to read the pixels

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            var file = Path.GetFileNameWithoutExtension(assetPath);
            settings.spriteBorder = file switch
            {
                "ui_rounded" or "ui_rounded_outline" => new Vector4(20, 20, 20, 20),
                "ui_shadow" => new Vector4(40, 40, 40, 40),
                _ => Vector4.zero,
            };
            importer.SetTextureSettings(settings);
            importer.wrapMode = file == "ui_hazard" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        }
    }
}
