using System.Collections.Generic;

public interface ISaveSystem
{
	/// <summary>
	/// Saves a data object to a specific slot.
	/// </summary>
	void Save<T>( string fileName, int slot, T data );

	/// <summary>
	/// Loads a data object from a specific slot.
	/// </summary>
	T Load<T>( string fileName, int slot );

	/// <summary>
	/// Checks if a save file exists for a specific slot.
	/// </summary>
	bool Exists( string fileName, int slot );

	/// <summary>
	/// Deletes a save file for a specific slot.
	/// </summary>
	void Delete( string fileName, int slot );
}
