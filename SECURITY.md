# Security policy

This library parses untrusted network input and handles login credentials, so reports are welcome.

Please report a vulnerability privately through GitHub: **Security > Report a vulnerability** on
<https://github.com/th3snark/E2e4.Chess.Fics.Client>. Do not open a public issue for it. Include the version, what an attacker controls
(a server, a network position, another player) and a minimal reproduction. You can expect an acknowledgement within a
few days.

In scope: the framing, parser and client logic here (injection into outbound lines, unbounded memory use, crashes or hangs
on crafted input, credentials or private text reaching logs). Out of scope: FICS itself, and the fact that FICS uses
unencrypted Telnet.
