<?php

// The admin password of the planet pages (index.php and convert.php).
//
// Copy this file to admin-config.php in the same folder (git ignores that name) and put the hash
// of a long password in it. Make the hash with:
//
//     php -r "echo password_hash( 'the password', PASSWORD_DEFAULT ), PHP_EOL;"
//
// Without admin-config.php, or with an empty hash, nobody can log in. Log in at index.php?login=1.

return [ 'passwordHash' => '' ];
