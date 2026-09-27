# Embedded ElevenLabs Agents SDK

Copied from https://github.com/elevenlabs/unity at commit 375e152dda6f7c29cb291525d7faa2872dad6e60
(the commit our packages-lock.json had pinned), using only the files listed in package.json "files".

Why embedded: installing from the git URL also imports the repo's TestProject/ folder, which has no .meta
files and makes Unity abort Android/iOS builds ("has no meta file"). To update: copy the same files from a
newer commit of the repo.
