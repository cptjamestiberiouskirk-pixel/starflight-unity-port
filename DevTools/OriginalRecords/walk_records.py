"""Reads the records ("instances") of the original game's STARB.COM, as s-macke/starflight-reverse's src/extract/instance.c walks them.

usage: python walk_records.py <STARB.COM> [--class N] [--from N] [--to N] [--races]

  with no option        the first 60 nodes of the tree, with their class, species and address
  --class N             every record of that class (0x34 is ORIGINATOR, a race), with its field bytes
  --from N --to N       the field bytes to show (offsets from the record's start; default 0x0b to 0x1a)
  --races               the race records' conversation fields (COMM-OV), one row per race

Read the original's files where they are, never change them: the new game's records are in the GOG install's STARFLT/START/STARB.COM.
"""
import argparse

# the races by their species (the port's GameData.Race values)
RACES = { 1: 'Elowan', 2: 'Gazurtoid', 3: 'Mechan', 4: 'Mysterion', 5: 'Nomad probe', 6: 'Spemin', 7: 'Thrynn', 8: 'Velox', 9: 'Uhlek', 10: 'Velox probe',
          11: 'Minstrel', 16: 'Human', 18: 'Enterprise', 19: 'Noah 9', 21: 'Interstel Police' }

ORIGINATOR = 0x34

def header( data, a ):
    # an 11-byte header: the next record, the previous one and the first child (3 bytes each), then the class and the species
    nxt = data[ a ] | data[ a + 1 ] << 8 | data[ a + 2 ] << 16
    child = data[ a + 6 ] | data[ a + 7 ] << 8 | data[ a + 8 ] << 16
    return nxt, child, data[ a + 9 ], data[ a + 10 ]

def walk_from( data, first, depth ):
    nodes = []
    addr = first

    for _ in range( 100000 ):
        nxt, child, cls, species = header( data, addr )
        nodes.append( ( depth, addr, cls, species ) )
        if child != 0:
            nodes.extend( walk_from( data, child, depth + 1 ) )
        if nxt == 0 or nxt == first:
            break
        addr = nxt

    return nodes

def signed( b ):
    return b - 256 if b > 127 else b

def main():
    parser = argparse.ArgumentParser( description = __doc__, formatter_class = argparse.RawDescriptionHelpFormatter )
    parser.add_argument( 'starb' )
    parser.add_argument( '--class', dest = 'cls', type = lambda s: int( s, 0 ) )
    parser.add_argument( '--from', dest = 'start', type = lambda s: int( s, 0 ), default = 0x0b )
    parser.add_argument( '--to', dest = 'end', type = lambda s: int( s, 0 ), default = 0x1a )
    parser.add_argument( '--races', action = 'store_true' )
    args = parser.parse_args()

    data = open( args.starb, 'rb' ).read()
    nodes = walk_from( data, 0x1006, 0 )

    if args.races:
        # the ORIGINATOR fields of COMM-OV: TC 0x0b, T% (talkativeness) 0x0c, CSF 0x0d, BASE-EDL 0x12, the EDL thresholds AET, BET, CET 0x13 to 0x15
        # (how many of them the EDL reaches: 0 friendly, 1 neutral, 2 hostile, 3 fight), the posture effects OPE, FPE, HPE 0x16 to 0x18 (signed), MSW 0x19
        print( '%-17s %4s %4s %4s %5s %4s %4s %4s %5s %5s %5s %4s' % ( 'race', 'TC', 'T%', 'CSF', 'EDL', 'AET', 'BET', 'CET', 'OPE', 'FPE', 'HPE', 'MSW' ) )
        for depth, addr, cls, species in nodes:
            if cls == ORIGINATOR:
                f = lambda o: data[ addr + o ]
                print( '%-17s %4d %4d %4d %5d %4d %4d %4d %5d %5d %5d %4d' % ( RACES.get( species, 'species %d' % species ), f( 0x0b ), f( 0x0c ), f( 0x0d ),
                    f( 0x12 ), f( 0x13 ), f( 0x14 ), f( 0x15 ), signed( f( 0x16 ) ), signed( f( 0x17 ) ), signed( f( 0x18 ) ), f( 0x19 ) ) )
        return

    if args.cls is None:
        for depth, addr, cls, species in nodes[ :60 ]:
            print( '  ' * depth + 'class=0x%02x species=%d @0x%06x' % ( cls, species, addr ) )
        print( len( nodes ), 'records' )
        return

    for depth, addr, cls, species in nodes:
        if cls == args.cls:
            print( '@0x%06x species=%3d bytes 0x%02x..0x%02x: %s' % ( addr, species, args.start, args.end,
                ' '.join( '%3d' % b for b in data[ addr + args.start:addr + args.end + 1 ] ) ) )

if __name__ == '__main__':
    main()
