using UnityEngine;
using System;
using System.IO;

public class JsonSaveSystem : ISaveSystem
{
	// the directory the save files are kept in (null means Application.persistentDataPath)
	readonly string m_directory;

	public JsonSaveSystem( string directory = null )
	{
		m_directory = directory;
	}

	private string GetPath( string fileName, int slot )
	{
		return Path.Combine( m_directory ?? Application.persistentDataPath, $"{fileName}{slot}.json" );
	}

	// the save from before the last one (every save keeps the file it replaces)
	private string GetBackupPath( string path )
	{
		return path + ".bak";
	}

	// a new save is written here first and only then swapped in
	private string GetTempPath( string path )
	{
		return path + ".tmp";
	}

	// a save that could not be read is moved here, so the next save can't destroy it
	private string GetCorruptPath( string path )
	{
		return path + ".corrupt";
	}

	public void Save<T>( string fileName, int slot, T data )
	{
		string path = GetPath( fileName, slot );
		string tempPath = GetTempPath( path );
		string backupPath = GetBackupPath( path );
		string json = JsonUtility.ToJson( data, true );

		// write the whole save to a temporary file first - a crash or a full disk in the middle of the write then can't damage the save we already have
		File.WriteAllText( tempPath, json );

		// is there a save to replace?
		if ( File.Exists( path ) )
		{
			try
			{
				// yes - swap the new save in and keep the old one as the backup (this is a single step on file systems that support it)
				File.Replace( tempPath, path, backupPath );
			}
			catch ( Exception exception )
			{
				// the swap is not available here - do the same thing in separate steps
				Debug.LogWarning( $"Could not swap the save file in one step ({exception.Message}) - copying instead" );

				if ( File.Exists( path ) )
				{
					File.Copy( path, backupPath, true );
				}

				File.Copy( tempPath, path, true );
				File.Delete( tempPath );
			}
		}
		else
		{
			// no - the temporary file simply becomes the save
			File.Move( tempPath, path );
		}

		Debug.Log( $"Saved data to {path}" );
	}

	public T Load<T>( string fileName, int slot )
	{
		string path = GetPath( fileName, slot );

		// try the save itself first
		if ( TryLoad( path, out T data ) )
		{
			return data;
		}

		// is there a save that we could not read?
		if ( File.Exists( path ) )
		{
			// yes - move it out of the way so the next save doesn't overwrite it (someone may still be able to rescue it)
			string corruptPath = GetCorruptPath( path );

			try
			{
				if ( File.Exists( corruptPath ) )
				{
					File.Delete( corruptPath );
				}

				File.Move( path, corruptPath );

				Debug.LogWarning( $"Save file {path} could not be read - it has been kept as {corruptPath}" );
			}
			catch ( Exception exception )
			{
				Debug.LogWarning( $"Save file {path} could not be read and could not be moved aside - {exception.Message}" );
			}
		}
		else
		{
			Debug.LogWarning( $"Save file not found at {path}" );
		}

		// fall back to the save from before the last one
		string backupPath = GetBackupPath( path );

		if ( TryLoad( backupPath, out data ) )
		{
			Debug.LogWarning( $"Loaded the backup save {backupPath}" );

			return data;
		}

		return default;
	}

	// read and parse a save file - returns false if it is missing, can't be read, or doesn't contain a save
	private bool TryLoad<T>( string path, out T data )
	{
		data = default;

		if ( !File.Exists( path ) )
		{
			return false;
		}

		try
		{
			string json = File.ReadAllText( path );

			data = JsonUtility.FromJson<T>( json );
		}
		catch ( Exception exception )
		{
			Debug.LogWarning( $"Could not read {path} - {exception.Message}" );

			data = default;

			return false;
		}

		// an empty file parses to nothing
		return data != null;
	}

	public bool Exists( string fileName, int slot )
	{
		string path = GetPath( fileName, slot );

		// a slot that only has its backup left still has something to load
		return File.Exists( path ) || File.Exists( GetBackupPath( path ) );
	}

	public void Delete( string fileName, int slot )
	{
		string path = GetPath( fileName, slot );

		// the backup and any half-written save go too (otherwise the slot would come back from its backup)
		string[] files = { path, GetBackupPath( path ), GetTempPath( path ) };

		foreach ( string file in files )
		{
			if ( File.Exists( file ) )
			{
				File.Delete( file );

				Debug.Log( $"Deleted save file at {file}" );
			}
		}
	}
}
