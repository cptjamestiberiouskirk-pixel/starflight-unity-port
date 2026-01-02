using UnityEngine;
using System.IO;

public class JsonSaveSystem : ISaveSystem
{
	private string GetPath( string fileName, int slot )
	{
		return Path.Combine( Application.persistentDataPath, $"{fileName}{slot}.json" );
	}

	public void Save<T>( string fileName, int slot, T data )
	{
		string path = GetPath( fileName, slot );
		string json = JsonUtility.ToJson( data, true );
		File.WriteAllText( path, json );
		Debug.Log( $"Saved data to {path}" );
	}

	public T Load<T>( string fileName, int slot )
	{
		string path = GetPath( fileName, slot );
		if ( !File.Exists( path ) )
		{
			Debug.LogWarning( $"Save file not found at {path}" );
			return default;
		}

		string json = File.ReadAllText( path );
		return JsonUtility.FromJson<T>( json );
	}

	public bool Exists( string fileName, int slot )
	{
		return File.Exists( GetPath( fileName, slot ) );
	}

	public void Delete( string fileName, int slot )
	{
		string path = GetPath( fileName, slot );
		if ( File.Exists( path ) )
		{
			File.Delete( path );
			Debug.Log( $"Deleted save file at {path}" );
		}
	}
}
