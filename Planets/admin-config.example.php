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
// to an hour, and a day without failures forgets them. Deleting the file forgets them at once and
// opens the login. While the file cannot be used the login stays closed, and the server's error log
// says why: the folder cannot be written or locked, or the file is a link, has another name too, or
// is another user's (in a shared temporary folder anyone can make it first). A file of the web
// server's that others can write is made writable by it alone. On a shared server, or where the
// temporary folder is cleared often, name a file in a folder of its own that only the web server
// can write and that it does not serve:
//
//     'throttleFile' => '/var/lib/starflight-planets/admin-logins.json',

return [ 'passwordHash' => '' ];
