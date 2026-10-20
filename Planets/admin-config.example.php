<?php

// The admin password of the planet pages (index.php and convert.php).
//
// Copy this file to admin-config.php in the same folder (git ignores that name) and put the hash
// of a long password in it. Make the hash with:
//
//     php -r "echo password_hash( 'the password', PASSWORD_DEFAULT ), PHP_EOL;"
//
// Without admin-config.php, or with an empty hash, nobody can log in. Log in at index.php?login=1.
//
// Failed logins are counted in a file in the system's temporary folder: after 5 from one address,
// or 20 from all together, the login closes for 30 seconds, doubling with each further failure up
// to an hour, and a day without failures forgets them. To keep the counts elsewhere (say, where the
// temporary folder is cleared often), name a file the web server can write that it does not serve:
//
//     'throttleFile' => '/var/lib/starflight-planets/admin-logins.json',

return [ 'passwordHash' => '' ];
