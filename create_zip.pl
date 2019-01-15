#!/usr/bin/perl
use warnings;
use strict;
use File::Path qw (rmtree);

rmtree "binaries";
mkdir "binaries";

chdir "builds";

system("..\\tools\\7z.exe", "a", "-r" , "builds.zip", "*.exe", "*.pdb", "version.txt") eq 0 or die("failed creating builds.zip");
system("move", "builds.zip", "..\\binaries");
