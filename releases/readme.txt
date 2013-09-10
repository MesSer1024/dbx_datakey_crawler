DatakeyCrawler

Intention: Make it possible to bridge dataKey-identifiers inside CPP-screens (FRONTEND_FOOBAR) to DBX identifiers inside FrostEd (-1285919) and see what keys that are used and where they are used.

Source code: https://github.com/MesSer1024/dbx_datakey_crawler


-----------------------------------------------
-----------------------------------------------
Changelog:
--------------------------
  1.5
--------------------------
- Increased performance by improving thread reading and I/O (Avg. search time on UI-files (approx. 23k files) down from ~60s to ~15s)
- Possible to filter results given the actual integer value of a datakey
- Removed a lot of "fake matches" for situations similar to (UI_FOO_BAR & UI_FOO_BARITEM) which previously reported UI_FOO_BARITEM as being used by UI_FOO_BAR)
- Outputting all the "fake matches" into a seperate file for further error investigations in case this messes something up
--------------------------
  1.4
--------------------------
- Made it possible to save/load a search to make it faster to use program when user has not done any sync changes etc.
- Improved GUI of program and made it possible to rearrange lists in view
- fixed bug with line numbers being wrong (windows understood \n\r as 2 line breaks instead of 1)
--------------------------
  1.3
--------------------------
- Added more options to config.ini and changed format
- Introduced "suspects" that are keys that has references but doesn't seem to have any impact on the system (only setData-calls)
- now using mono-font (Mono Segoe UI)
- Now showing how many references each key has in gui
- Major refactoring on underlying datastructure to improve usage

--------------------------
  1.2
--------------------------
- Added threading (60% performance increase on my machine)
- Fixed a bug where all __*.txt-files missed last chunk of data due to missed "stream.flush()"-call

--------------------------
  1.1
--------------------------
- Added support for navigating through cpp-files as well
- 3 files in output folder (unused keys, files when used, lines when used)
- reads folder paths and other variables from config file
- using line filtering to speed up parsing [this also removed false hits when a GUID happened to contain the same values as a hashkey]

--------------------------
  1.0
--------------------------
Going through dbx files and finding reference for a specific guid