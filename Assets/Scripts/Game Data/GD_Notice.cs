
using System;

[Serializable]

public class GD_Notice
{
	public int m_id;
	public string m_stardate;
	public string m_message;

	// the length in bytes of each paragraph's encoded text in the shipped game (its STRING record's byte 0x0b, START\STARB.COM): the original pauses after a
	// paragraph for 300 ms a byte (OP-OV WE912)
	public int[] m_paragraphLengths;
}
