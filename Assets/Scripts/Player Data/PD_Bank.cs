
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
	}
}
