#!/usr/bin/perl
use warnings;
use strict;
use File::Path qw (rmtree);

rmtree "binaries";
mkdir "binaries";

chdir "builds";

system("zip -rq builds.zip . -i *.exe *.pdb version.txt") && die("failed creating builds.zip");
system("mv builds.zip ../binaries");
