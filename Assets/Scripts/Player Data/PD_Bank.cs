
using System;
using System.Collections.Generic;

[Serializable]

public class PD_Bank
{
	[Serializable]

	public class Transaction
	{
		public string m_stardate;
		public string m_description;
		public string m_amount;

		public Transaction( string stardate, string description, string amount )
		{
			m_stardate = stardate;
			m_description = description;
			m_amount = amount;
		}
	}

	// what the player has in the bank at the start of the game - the original game starts with 12,000 M.U. (the first starport notice says so)
	public const int c_startingBalance = 12000;

	// in the editor a new game starts rich, so that everything can be bought and tried out
	public const int c_editorStartingBalance = 1000000;

	public int m_currentBalance;
	public List<Transaction> m_transactionList;

	// the bank's interest, as the original's (BANK-OV in the decompiled original, INT%): 12% a year of the balance (a year of the original's calendar, 300 days)
	public const int c_interestPercent = 12;

	// the day the interest was paid up to (the original's PORTDAT), and whether it is known (save files from before the interest have neither, and start counting
	// the next time the interest is worked out, with nothing paid for the days before)
	public int m_interestDay;
	public bool m_interestDayIsSet;

	// the balance a new game starts with - a build gets the original balance, only the editor gets the rich one
	public static int GetStartingBalance( bool inEditor )
	{
		return inEditor ? c_editorStartingBalance : c_startingBalance;
	}

	public void Reset()
	{
		// reset the bank balance
		m_currentBalance = GetStartingBalance( UnityEngine.Application.isEditor );

		// create a new transactions list
		m_transactionList = new List<Transaction>
		{
			// add the first transaction (game purchase)
			new Transaction( "4620-01-01", "Game purchase", "200-" )
		};

		// the interest counts from the first day of the game
		m_interestDay = 0;
		m_interestDayIsSet = true;
	}

	// the interest the balance has earned from the day it was last paid to this day, in whole M.U. as the original works it out (the balance times 12 over 100,
	// then times the days over 300, each rounded down) - 0 for no days, and for a save file from before the interest, whose day is not known
	public int GetInterestDue( int today )
	{
		if ( !m_interestDayIsSet || ( m_currentBalance <= 0 ) )
		{
			return 0;
		}

		var days = today - m_interestDay;

		if ( days <= 0 )
		{
			return 0;
		}

		var interest = (long) m_currentBalance * c_interestPercent / 100 * days / PD_General.c_daysPerYear;

		return (int) System.Math.Min( interest, int.MaxValue - (long) m_currentBalance );
	}
}
