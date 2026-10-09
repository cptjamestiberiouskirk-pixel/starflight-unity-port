
using UnityEngine;

using System;
using System.Collections.Generic;

[Serializable] public class PD_ShipsLog
{
	public enum AlienComm
	{
		Themselves,
		OtherRaces,
		GeneralInfo,
		OldEmpire,
		TheAncients,
		Count,
		First = Themselves,
		Last = TheAncients
	}

	[Serializable] public class Entry
	{
		public int m_id;
		public string m_stardate;
		public string m_header;
		public string m_message;

		public Entry( int id, string stardate, string header, string message )
		{
			m_id = id;
			m_stardate = stardate;
			m_header = header;
			m_message = message;
		}
	}

	// JsonUtility can't serialize an array of lists, so each alien comm subject's list is wrapped in a serializable class
	[Serializable] public class EntryList
	{
		public List<Entry> m_entryList;
	}

	[SerializeField] public List<Entry> m_starportNotices;
	[SerializeField] public List<Entry> m_foundMessages;
	[SerializeField] public List<Entry> m_planetLogs;
	[SerializeField] public EntryList[] m_alienComms;

	public void Reset()
	{
		// allocate memory
		m_starportNotices = new List<Entry>();
		m_foundMessages = new List<Entry>();
		m_planetLogs = new List<Entry>();

		m_alienComms = null;

		ValidateAlienComms();
	}

	// get the alien comms the player has seen for this subject
	public List<Entry> GetAlienComms( AlienComm alienComm )
	{
		ValidateAlienComms();

		return m_alienComms[ (int) alienComm ].m_entryList;
	}

	// make sure there is a list for every alien comm subject (save files from before they were saved don't have them)
	void ValidateAlienComms()
	{
		if ( ( m_alienComms == null ) || ( m_alienComms.Length != (int) AlienComm.Count ) )
		{
			m_alienComms = new EntryList[ (int) AlienComm.Count ];
		}

		for ( var i = AlienComm.First; i <= AlienComm.Last; i++ )
		{
			if ( m_alienComms[ (int) i ] == null )
			{
				m_alienComms[ (int) i ] = new EntryList();
			}

			if ( m_alienComms[ (int) i ].m_entryList == null )
			{
				m_alienComms[ (int) i ].m_entryList = new List<Entry>();
			}
		}
	}

	public bool AddPlanetLog( int planetId, string stardate, string header, string message )
	{
		// ensure m_planetLogs is initialized (for existing save files)
		if ( m_planetLogs == null )
		{
			m_planetLogs = new List<Entry>();
		}

		// check if this planet has already been logged
		foreach ( var entry in m_planetLogs )
		{
			if ( entry.m_id == planetId )
			{
				// already logged
				return false;
			}
		}

		// add new planet log entry
		var newEntry = new Entry( planetId, stardate, header, message );
		m_planetLogs.Add( newEntry );

		return true;
	}

	// call this when a message has been recorded in a ruin - "any messages you find are identified by the date found" (the manual, page 21)
	// returns false if this message is in the ships log already
	public bool AddFoundMessage( int messageId, string stardate, string header, string message )
	{
		// save files from before the messages were found have no list
		if ( m_foundMessages == null )
		{
			m_foundMessages = new List<Entry>();
		}

		foreach ( var entry in m_foundMessages )
		{
			if ( entry.m_id == messageId )
			{
				return false;
			}
		}

		m_foundMessages.Add( new Entry( messageId, stardate, header, message ) );

		return true;
	}

	// true if this message has been recorded already
	public bool HasFoundMessage( int messageId )
	{
		if ( m_foundMessages == null )
		{
			return false;
		}

		foreach ( var entry in m_foundMessages )
		{
			if ( entry.m_id == messageId )
			{
				return true;
			}
		}

		return false;
	}

	public void AddStarportNotice( int noticeId )
	{
		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		// go through the starport notices the player has already seen
		for ( var i = 0; i < m_starportNotices.Count; i++ )
		{
			// is this the same notice?
			if ( m_starportNotices[ i ].m_id == noticeId )
			{
				// yes - we've already added it to the ship log so don't worry about it
				return;
			}
		}

		// get the notice to add from the game data
		var noticeToAdd = gameData.m_noticeList[ noticeId ];

		// convert the stardate into the form the game shows (these two are saved, so they must not depend on the date format of the computer's region)
		var stardate = PD_General.GetDisplayStardate( noticeToAdd.m_stardate );
		var header = stardate;

		// add this new notice to the ships log
		var entry = new Entry( noticeId, stardate, header, noticeToAdd.m_message );

		m_starportNotices.Add( entry );
	}

	// call this when a save file has been loaded - the starport notices in the ships log used to be dated in the date format of the computer's region, and those dates were saved
	public void ValidateStarportNoticeDates()
	{
		// save files from before the ships log have no list
		if ( m_starportNotices == null )
		{
			return;
		}

		// get to the game data
		var gameData = DataController.m_instance.m_gameData;

		foreach ( var entry in m_starportNotices )
		{
			// skip an entry whose notice the game data does not have
			if ( ( entry == null ) || ( entry.m_id < 0 ) || ( entry.m_id >= gameData.m_noticeList.Length ) )
			{
				continue;
			}

			// date it again from the stardate of the notice
			entry.m_stardate = PD_General.GetDisplayStardate( gameData.m_noticeList[ entry.m_id ].m_stardate );
			entry.m_header = entry.m_stardate;
		}
	}

	// call this when a save file made before the stardates were in the original's calendar has been loaded - what the aliens said and the planets that were logged
	// are dated day.hour-month-year in the real-world calendar there (the starport notices are dated from the game data, see ValidateStarportNoticeDates)
	public void ConvertRealCalendarDates()
	{
		if ( m_planetLogs != null )
		{
			foreach ( var entry in m_planetLogs )
			{
				if ( entry != null )
				{
					entry.m_stardate = PD_General.ConvertRealCalendarDHMY( entry.m_stardate );
				}
			}
		}

		ValidateAlienComms();

		foreach ( var entryList in m_alienComms )
		{
			foreach ( var entry in entryList.m_entryList )
			{
				if ( entry != null )
				{
					entry.m_stardate = PD_General.ConvertRealCalendarDHMY( entry.m_stardate );
				}
			}
		}
	}

	public void AddAlienComm( GD_Comm comm, string message )
	{
		// get to the player data
		var playerData = DataController.m_instance.m_playerData;

		// calculate the index
		int index = (int) comm.m_subject - (int) GD_Comm.Subject.Themselves;

		// validate index is in bounds
		if ( index < 0 || index >= (int) AlienComm.Count )
		{
			Debug.LogWarning( $"AddAlienComm: Invalid subject index {index} for subject {comm.m_subject}" );
			return;
		}

		// which subject?
		var alienComms = GetAlienComms( (AlienComm) index );

		// go through the alien comms the player has already seen
		for ( var i = 0; i < alienComms.Count; i++ )
		{
			// is this the same alien comm?
			if ( alienComms[ i ].m_id == comm.m_id )
			{
				// yes - is the message the same? (could be different due to garbling and comm officer skill level)
				if ( alienComms[ i ].m_message == message )
				{
					// yes - we've already added it to the ship log so don't worry about it
					return;
				}
			}
		}

		// get the stardate (and hour)
		var stardate = playerData.m_general.m_currentStardateDHMY;

		// build the header
		var header = "Alien Species #" + (int) comm.m_race;

		// add this new notice to the ships log
		var entry = new Entry( comm.m_id, stardate, header, message );

		alienComms.Add( entry );
	}
}
