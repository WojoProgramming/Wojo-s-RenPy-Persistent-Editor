# Wojo's Ren'Py Persistent Editor

Hi!

Thank you for downloading my editor for `persistent` files used by games created with Ren'Py. I hope this program helps someone, whether you want to fix a single flag, restore previously unlocked content, or simply see what the game has saved.

The project is developed by **WojoProgramming** and released as open-source software.

> [!WARNING]
> The `persistent` file stores things such as permanent progress, unlocked content, previously read text, and game settings. An incorrect modification may cause problems with the game. Always create a backup copy of the file before making any changes.

## Important information about antivirus software

The program contains helper tools built from Python code. Some antivirus programs, especially Avast, may treat such files as suspicious even when they are harmless.

For those who do not care:

1. Disable your antivirus.

Simple? Simple.

For the cautious:

I do not recommend disabling your entire antivirus program. Instead:

1. Download the program only from the project's official GitHub repository.
2. Make sure the downloaded version comes from the **Releases** section of this repository.
3. If you receive a warning, scan the archive or the specific file with an additional scanner.
4. If you trust the downloaded version, add only the program folder or the specific file reported by your antivirus to its exceptions.
5. If you are unsure where the file came from, do not run it.

## How to use the editor

1. Create a backup copy of the original `persistent` file.
2. Launch the application.
3. Open the **Preview** or **Editing** tab.
4. Click the file selection button and choose the `persistent` file belonging to the selected game.
5. In the **Preview** tab, you can safely browse variables and their values.
6. In the **Editing** tab, you can modify supported values.
7. Click **Save** to prepare the modified file.
8. Click **Download** to choose where the file should be saved.

The program should not overwrite the selected source file. Even so, creating a backup is still a very good idea. This is a `persistent` file. It is better to have one backup too many than one too few.

## Variable types

The variable type is displayed next to its name, for example:

```text
player_name (str)
chapter (int)
good_ending (bool)
```

Do not change a value into a different type. If a variable was an integer, it must remain an integer. A text box may look innocent, but Python has its own opinion on the matter.

| Type        | Meaning                                                         | Example of a valid value        |
| ----------- | --------------------------------------------------------------- | ------------------------------- |
| `bool`      | A Boolean value: true or false                                  | `True` or `False`               |
| `str`       | Plain text                                                      | `Food`                          |
| `int`       | An integer                                                      | `12`, `0`, `-5`                 |
| `float`     | A number with a decimal part                                    | `3.14`, `-0.5`                  |
| `NoneType`  | No value                                                        | `None`                          |
| `complex`   | A complex number; encountered very rarely                       | `2+3j`                          |
| `bytes`     | Byte data                                                       | `b'text'`                       |
| `bytearray` | A mutable sequence of bytes                                     | `bytearray(b'text')`            |
| `list`      | A list of elements; order and duplicates matter                 | `[1, 2, 3]`                     |
| `tuple`     | An ordered collection of values, usually with a fixed structure | `(1280, 720)`                   |
| `set`       | A collection of unique elements with no guaranteed order        | `{'a', 'b'}`                    |
| `frozenset` | An immutable version of a set                                   | `frozenset({'a', 'b'})`         |
| `dict`      | A dictionary containing key-value pairs                         | `{'ending': True, 'score': 10}` |

The most important rules:

* use `True` or `False` for Boolean values;
* do not enter text or a decimal number into an `int` variable;
* the decimal separator for a `float` is a period, for example `1.5`, not `1,5`;
* do not remove brackets, commas, or quotation marks from complex values unless you know what they do;
* some internal Ren'Py objects can only be viewed because the editor intentionally prevents them from being saved.

## Where to find the `persistent` file

The name of the game's directory depends on the `config.save_directory` value chosen by its developer. It usually resembles the game's name followed by a sequence of digits.

### Windows

1. Press `Win + R`.
2. Enter `%APPDATA%\RenPy` and confirm.
3. Open the folder corresponding to the game you are looking for.
4. Find the file named `persistent`.

Typical path:

```text
C:\Users\USERNAME\AppData\Roaming\RenPy\GAME_NAME\persistent
```

### macOS

1. Open Finder.
2. Select **Go → Go to Folder...** or use the `Shift + Command + G` shortcut.
3. Enter `~/Library/RenPy/`.
4. Open the game's folder and find the `persistent` file.

Typical path:

```text
~/Library/RenPy/GAME_NAME/persistent
```

### Linux

1. Enable the display of hidden files in your file manager, usually by pressing `Ctrl + H`.
2. Open your home directory.
3. Open `.renpy`, then open the game's folder.
4. Find the `persistent` file.

Typical path:

```text
~/.renpy/GAME_NAME/persistent
```

Ren'Py may also store a copy of persistent data inside the game's own save directory. If you cannot find the correct file in the system location, check the `game/saves` folder inside the game's installation directory. Depending on the Ren'Py version and the game's configuration, the data may exist in both locations.

An official explanation of how this path is created can be found in the [Ren'Py ](https://www.renpy.org/doc/html/config.html#var-config.save_directory)[`config.save_directory`](https://www.renpy.org/doc/html/config.html#var-config.save_directory)[ documentation](https://www.renpy.org/doc/html/config.html#var-config.save_directory).

## How to find the game files

### Steam

1. Open your Steam Library.
2. Right-click the game.
3. Select **Manage → Browse local files**.
4. Open the `game` folder.

### Other platforms and portable versions

Find the folder containing the game's executable file. Ren'Py data is usually located in the `game` subfolder.

On macOS, you may need to right-click the application, select **Show Package Contents**, and then locate the directory containing the game files.

## How to determine what flags do

Variable names may tell you a lot, but they are not always obvious. You can search for flag definitions in the game's source files:

* `.rpy` - readable Ren'Py script files;
* `.rpyc` - compiled scripts that cannot be opened with an ordinary text editor;
* `.rpa` - archives containing scripts, images, sounds, and other assets.

If the game contains normal `.rpy` files, you can search through them using a code editor. If the files are stored inside an `.rpa` archive, you will need to extract them first.

> [!IMPORTANT]
> Do not publish someone else's assets or game code. THAT IS THEFT >:( The tools described below are intended for personal analysis, debugging, translations, and other permitted uses.

## Extracting `.rpa` archives

You can use one of the following tools.

### Option 1: unrpa

[`unrpa`](https://github.com/lattyware/unrpa) is a command-line tool designed to extract Ren'Py archives.

Installation using Python:

```bash
python -m pip install unrpa
```

On some systems, the command may be named `python3` or `py -3`.

Example archive extraction:

```bash
unrpa -mp OUTPUT archive.rpa
```

If the `unrpa` command cannot be found, you can try:

```bash
python -m unrpa -mp OUTPUT archive.rpa
```

The exact options may differ between versions, so if you encounter problems, use `unrpa --help` and check the tool's documentation.

### Option 2: RPA Extract

If you prefer a simpler tool for Windows, you can use [RPA Extract](https://iwanplays.itch.io/rpaex). According to its creator, you only need to drag the `.rpa` file or files onto `rpaExtract.exe`, and the program will begin extracting them.

Only download tools from their creators' official websites. Some extractors found on random websites may contain outdated or modified executable files.

## After extracting the files

Search for the name of the variable displayed by Persistent Editor inside the `.rpy` files. Pay attention to places where the variable:

* is assigned a value;
* is checked inside an `if` statement;
* unlocks a scene, gallery entry, or ending;
* is added to a list or set.

Not every variable is a story flag. Some variables may control settings, the engine's internal state, or other technical game data.

## Reporting bugs

If the program cannot open a file, displays a variable incorrectly, or creates a file that the game refuses to accept, report the problem through the **Bugs** tab in the application.

It is helpful to include:

* your operating system and its version;
* the application version;
* the name and version of the game;
* the error message;
* a description of what you did before the problem occurred;
* a screenshot, if you want to include one.

Do not publish your own `persistent` file if it may contain private information, unless you knowingly agree to share it for diagnostic purposes.

## Final words

This tool was created because manually digging through thousands of variables does not sound like a particularly good plan. It sounds more like a fast track to losing your sanity.”

I hope it saves you some time and prevents at least a few unnecessary disasters.

Happy editing, and once again: **create a backup**.

**WojoProgramming**
