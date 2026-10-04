
using UnityEditor;
using UnityEngine;

using System.IO;

public class EditorUtilitySaveFilePanel : MonoBehaviour
{
	[MenuItem( "Starflight Remake/Save Texture to File" )]
	static void Apply()
	{
		Texture2D texture = Selection.activeObject as Texture2D;

		if ( texture == null )
		{
			EditorUtility.DisplayDialog( "Select Texture", "You Must Select a Texture first!", "Ok" );
			return;
		}

		var path = EditorUtility.SaveFilePanel( "Save texture as PNG", "", texture.name + ".png", "png" );

		if ( path.Length != 0 )
		{
			Texture2D readableTexture = null;

			// the copy we make when the texture cannot be read as it is (it is ours to destroy again)
			Texture2D temporaryTexture = null;

			// a texture can only be turned into a png as it is if its pixels can be read and it is in a format a png can be made from
			if ( texture.isReadable && ( ( texture.format == TextureFormat.ARGB32 ) || ( texture.format == TextureFormat.RGB24 ) ) )
			{
				readableTexture = texture;
			}
			else
			{
				RenderTexture renderTexture = RenderTexture.GetTemporary( texture.width, texture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB );

				Graphics.Blit( texture, renderTexture );

				RenderTexture previous = RenderTexture.active;

				RenderTexture.active = renderTexture;

				temporaryTexture = new Texture2D( texture.width, texture.height );

				temporaryTexture.ReadPixels( new Rect( 0, 0, renderTexture.width, renderTexture.height ), 0, 0 );

				temporaryTexture.Apply();

				RenderTexture.active = previous;

				RenderTexture.ReleaseTemporary( renderTexture );

				readableTexture = temporaryTexture;
			}

			var pngData = readableTexture.EncodeToPNG();

			if ( pngData != null )
			{
				File.WriteAllBytes( path, pngData );
			}

			// the copy has done its job
			if ( temporaryTexture != null )
			{
				DestroyImmediate( temporaryTexture );
			}
		}
	}
}
