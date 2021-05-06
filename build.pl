#/usr/bin/perl
use strict;
use warnings;
use File::Basename;
use File::Path;

my $root = File::Spec->rel2abs(dirname($0));
my $outDirectory = "$root\\builds";
mkpath($outDirectory);

# write build version.txt to output dir
open(my $versionFile, '>', "$outDirectory\\version.txt") or die("Unable to write build information to version.txt");
print $versionFile "Repository: https://github.com/Unity-Technologies/DevicePortalTool\n";
print $versionFile "Branch: " . $ENV{GIT_BRANCH} . "\n";
print $versionFile "Revision: " . $ENV{GIT_REVISION};
close $versionFile;

my $msbuildPath = "\"C:\\Program Files (x86)\\MSBuild\\14.0\\Bin\\msbuild.exe\"";

system("$msbuildPath $root\\DevicePortalTool.sln /property:OutDir=$outDirectory;Configuration=Release /verbosity:detailed");
