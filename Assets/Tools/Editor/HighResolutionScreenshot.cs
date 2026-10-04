
using UnityEngine;
using UnityEditor;

using System.IO;

[ExecuteInEditMode]
public class Screenshot : EditorWindow
{
	int m_superSize = 1;

	string m_filename = "";

	private void OnEnable()
	{
		m_filename = Application.dataPath + "/Image.png";
	}

	[MenuItem( "Starflight Remake/High Resolution Screenshot" )]
	public static void ShowWindow()
	{
		EditorWindow editorWindow = EditorWindow.GetWindow( typeof( Screenshot ) );

		editorWindow.autoRepaintOnSceneChange = true;

		editorWindow.Show();

		editorWindow.titleContent = new GUIContent( "High Resolution Screenshot" );
	}

	void OnGUI()
	{
		EditorGUILayout.LabelField( "Super Size", EditorStyles.boldLabel );

		m_superSize = EditorGUILayout.IntSlider( "Scale", m_superSize, 1, 16 );

		EditorGUILayout.Space();

		if ( GUILayout.Button( "Take Screenshot", GUILayout.MinHeight( 60 ) ) )
		{
			var filename = EditorUtility.SaveFilePanel( "Image File Name", Path.GetDirectoryName( m_filename ), Path.GetFileName( m_filename ), "png" );

			// a cancelled dialog gives an empty name - keep the one we had (with an empty one the next click could not work out a folder and threw)
			if ( filename.Length > 0 )
			{
				m_filename = filename;

				ScreenCapture.CaptureScreenshot( m_filename, m_superSize );
			}
		}
	}
}
