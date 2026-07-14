from __future__ import annotations

import argparse
import base64
import codecs
import collections
import copyreg
import io
import json
import math
import pickle
import sys
import traceback
import types
import zlib
from pathlib import Path
from typing import Any


# Atrybuty tworzone wyłącznie przez dekoder. Nie są zmiennymi zapisanymi
# przez autora gry i dlatego nie powinny trafić na listę w interfejsie.
# Attributes created only by the decoder. They are not variables saved by the
# game developer and therefore should not appear in the editor interface.
DECODER_INTERNAL_FIELDS = {
    "_decoder_pickle_state",
    "_decoder_constructor_args",
    "_decoder_constructor_kwargs",
    "_decoder_mapping_items",
    "_decoder_list_items",
    "_decoder_set_items",
}


def debug(message: str, enabled: bool) -> None:
    """
    Wypisuje diagnostykę do stderr, nie zanieczyszczając JSON-a.
    Writes diagnostics to stderr without contaminating the JSON output.
    """
    if enabled:
        print(message, file=sys.stderr)


class StateMixin:
    """
    Przyjmuje stan obiektów zapisanych przez pickle/Ren'Py.
    Accepts the state of objects serialized by pickle/Ren'Py.
    """

    def __setstate__(self, state: Any) -> None:
        try:
            self._decoder_pickle_state = state
        except (AttributeError, TypeError):
            pass

        dictionaries: list[dict[Any, Any]] = []

        if isinstance(state, dict):
            dictionaries.append(state)
        elif isinstance(state, tuple):
            dictionaries.extend(
                part for part in state if isinstance(part, dict)
            )

        for state_dictionary in dictionaries:
            try:
                self.__dict__.update(state_dictionary)
            except AttributeError:
                # Nie każdy obiekt posiada __dict__, np. część obiektów
                # z __slots__. Zachowujemy wtedy sam stan dekodera.
                # Not every object has a __dict__, such as some objects using
                # __slots__. In that case, only the decoder state is retained.
                pass


class Persistent(StateMixin):
    pass


class RevertableObject(StateMixin):
    pass


class RevertableDict(StateMixin, dict):
    pass


class RevertableList(StateMixin, list):
    pass


class RevertableSet(StateMixin, set):
    pass


class Preferences(StateMixin):
    pass


class GenericPickleObject(StateMixin):
    """
    Bezpieczny obiekt zastępczy dla klas niedostępnych poza grą.
    Implementuje operacje używane przez pickle dla obiektów podobnych do
    słownika, listy i zbioru. Dzięki temu nieznana klasa nie wywoła od razu
    błędu "does not support item assignment" albo braku metody append.

    A safe placeholder for classes unavailable outside the game.
    It implements operations used by pickle for dictionary-like, list-like,
    and set-like objects. This prevents an unknown class from immediately
    causing a "does not support item assignment" or missing append error.
    """

    _decoder_original_module = "unknown"
    _decoder_original_name = "Unknown"

    def __new__(cls, *args: Any, **kwargs: Any):
        return object.__new__(cls)

    def __init__(self, *args: Any, **kwargs: Any) -> None:
        if args:
            self._decoder_constructor_args = args
        if kwargs:
            self._decoder_constructor_kwargs = kwargs

    def __setitem__(self, key: Any, value: Any) -> None:
        if not hasattr(self, "_decoder_mapping_items"):
            self._decoder_mapping_items = {}
        self._decoder_mapping_items[key] = value

    def append(self, value: Any) -> None:
        if not hasattr(self, "_decoder_list_items"):
            self._decoder_list_items = []
        self._decoder_list_items.append(value)

    def extend(self, values: Any) -> None:
        if not hasattr(self, "_decoder_list_items"):
            self._decoder_list_items = []
        self._decoder_list_items.extend(values)

    def add(self, value: Any) -> None:
        if not hasattr(self, "_decoder_set_items"):
            self._decoder_set_items = set()
        self._decoder_set_items.add(value)


class RenPyUnpickler(pickle.Unpickler):
    """
    Unpickler, który nie wymaga instalacji modułu Ren'Py ani kodu gry.
    An unpickler that does not require Ren'Py or the game's code to be installed.
    """

    def __init__(self, file: io.BytesIO, *, debug_enabled: bool = False):
        super().__init__(
            file,
            fix_imports=True,
            encoding="latin1",
            errors="strict",
        )
        self.debug_enabled = debug_enabled
        self.unknown_classes: set[str] = set()
        self._placeholder_classes: dict[tuple[str, str], type] = {}

    def _placeholder_class(self, module: str, name: str) -> type:
        key = (module, name)

        if key not in self._placeholder_classes:
            safe_name = "".join(
                character if character.isalnum() else "_"
                for character in f"{module}_{name}"
            )

            self._placeholder_classes[key] = type(
                f"Decoded_{safe_name}",
                (GenericPickleObject,),
                {
                    "_decoder_original_module": module,
                    "_decoder_original_name": name,
                },
            )

        return self._placeholder_classes[key]

    def find_class(self, module: str, name: str):
        qualified_name = f"{module}.{name}"
        debug(f"Loading class: {qualified_name}", self.debug_enabled)

        revertable_classes = {
            "RevertableObject": RevertableObject,
            "RevertableDict": RevertableDict,
            "RevertableList": RevertableList,
            "RevertableSet": RevertableSet,
        }

        # Spotykane są oba warianty nazw modułu, zależnie od wersji Ren'Py.
        # Both module name variants occur depending on the Ren'Py version.
        if module in ("renpy.python", "renpy.revertable"):
            if name in revertable_classes:
                return revertable_classes[name]

        if module == "renpy.persistent" and name == "Persistent":
            return Persistent

        if module == "renpy.preferences" and name == "Preferences":
            return Preferences

        safe_globals = {
            ("builtins", "set"): set,
            ("builtins", "frozenset"): frozenset,
            ("builtins", "list"): list,
            ("builtins", "dict"): dict,
            ("builtins", "tuple"): tuple,
            ("builtins", "str"): str,
            ("builtins", "bytes"): bytes,
            ("builtins", "bytearray"): bytearray,
            ("builtins", "int"): int,
            ("builtins", "float"): float,
            ("builtins", "bool"): bool,
            ("builtins", "complex"): complex,
            ("builtins", "object"): object,
            ("builtins", "slice"): slice,
            ("__builtin__", "set"): set,
            ("__builtin__", "frozenset"): frozenset,
            ("__builtin__", "list"): list,
            ("__builtin__", "dict"): dict,
            ("__builtin__", "tuple"): tuple,
            ("__builtin__", "str"): str,
            ("__builtin__", "unicode"): str,
            ("__builtin__", "bytes"): bytes,
            ("__builtin__", "bytearray"): bytearray,
            ("__builtin__", "int"): int,
            ("__builtin__", "long"): int,
            ("__builtin__", "float"): float,
            ("__builtin__", "bool"): bool,
            ("__builtin__", "complex"): complex,
            ("__builtin__", "object"): object,
            ("__builtin__", "slice"): slice,
            ("collections", "OrderedDict"): collections.OrderedDict,
            ("types", "SimpleNamespace"): types.SimpleNamespace,
            ("_codecs", "encode"): codecs.encode,
            ("copyreg", "_reconstructor"): copyreg._reconstructor,
            ("copy_reg", "_reconstructor"): copyreg._reconstructor,
        }

        safe_global = safe_globals.get((module, name))
        if safe_global is not None:
            return safe_global

        # Nie importujemy automatycznie modułów gry. Złośliwy pickle mógłby
        # w przeciwnym razie uruchomić dowolną funkcję podczas wczytywania.
        # Game modules are not imported automatically. Otherwise, a malicious
        # pickle could execute an arbitrary function while being loaded.
        self.unknown_classes.add(qualified_name)
        debug(
            f"Unknown class, using a placeholder: {qualified_name}",
            self.debug_enabled,
        )
        return self._placeholder_class(module, name)


def type_name(value: Any) -> str:
    """
    Zwraca nazwę typu przeznaczoną do pokazania obok nazwy zmiennej.
    Returns the type name intended for display next to the variable name.
    """
    if value is None:
        return "NoneType"

    value_class = type(value)
    original_module = getattr(
        value_class,
        "_decoder_original_module",
        None,
    )
    original_name = getattr(
        value_class,
        "_decoder_original_name",
        None,
    )

    if original_module and original_name:
        return f"{original_module}.{original_name}"

    return value_class.__name__


def editor_text(value: Any) -> str:
    """
    Zwraca tekst, który C# może bezpośrednio wstawić do pola TextBox.
    Returns text that C# can insert directly into a TextBox.
    """
    if isinstance(value, str):
        return value

    if value is None:
        return "None"

    try:
        return repr(value)
    except Exception:
        return f"<{type_name(value)}>"


def encode_value(value: Any, active_objects: set[int] | None = None) -> Any:
    """
    Zamienia typową wartość pickle na strukturę zgodną z JSON.
    Converts a typical pickle value into a JSON-compatible structure.
    """
    if active_objects is None:
        active_objects = set()

    if value is None or isinstance(value, (bool, str)):
        return {
            "type": type_name(value),
            "value": value,
        }

    if isinstance(value, int):
        return {
            "type": type_name(value),
            "value": value,
        }

    if isinstance(value, float):
        if math.isfinite(value):
            encoded_float: Any = value
        else:
            # JSON i System.Text.Json nie traktują NaN/Infinity jak zwykłych
            # liczb. Zachowujemy je więc jako ich dokładny zapis tekstowy.
            # JSON and System.Text.Json do not treat NaN/Infinity as ordinary
            # numbers, so their exact textual representation is preserved.
            encoded_float = repr(value)

        return {
            "type": type_name(value),
            "value": encoded_float,
        }

    if isinstance(value, complex):
        return {
            "type": type_name(value),
            "value": repr(value),
        }

    if isinstance(value, (bytes, bytearray)):
        return {
            "type": type_name(value),
            "encoding": "base64",
            "value": base64.b64encode(bytes(value)).decode("ascii"),
        }

    object_id = id(value)
    if object_id in active_objects:
        return {
            "type": type_name(value),
            "cycle": True,
        }

    active_objects.add(object_id)

    try:
        if isinstance(value, dict):
            return {
                "type": type_name(value),
                "entries": [
                    {
                        "key": encode_value(key, active_objects),
                        "value": encode_value(item, active_objects),
                    }
                    for key, item in value.items()
                ],
            }

        if isinstance(value, (list, tuple, set, frozenset)):
            return {
                "type": type_name(value),
                "items": [
                    encode_value(item, active_objects)
                    for item in value
                ],
            }

        if isinstance(value, GenericPickleObject):
            result: dict[str, Any] = {
                "type": type_name(value),
                "fields": encode_object_fields(value, active_objects),
            }

            mapping_items = getattr(
                value,
                "_decoder_mapping_items",
                None,
            )
            if mapping_items is not None:
                result["mapping"] = encode_value(
                    mapping_items,
                    active_objects,
                )

            list_items = getattr(value, "_decoder_list_items", None)
            if list_items is not None:
                result["items"] = [
                    encode_value(item, active_objects)
                    for item in list_items
                ]

            set_items = getattr(value, "_decoder_set_items", None)
            if set_items is not None:
                result["set_items"] = [
                    encode_value(item, active_objects)
                    for item in set_items
                ]

            return result

        if hasattr(value, "__dict__"):
            return {
                "type": type_name(value),
                "fields": encode_object_fields(value, active_objects),
            }

        return {
            "type": type_name(value),
            "value": editor_text(value),
            "fallback": True,
        }
    finally:
        active_objects.remove(object_id)


def encode_object_fields(
    value: Any,
    active_objects: set[int],
) -> list[dict[str, Any]]:
    fields = []

    for field_name, field_value in sorted(
        vars(value).items(),
        key=lambda item: str(item[0]),
    ):
        if field_name in DECODER_INTERNAL_FIELDS:
            continue

        fields.append(
            {
                "name": str(field_name),
                "value": encode_value(field_value, active_objects),
            }
        )

    return fields


def is_editor_collection(value: Any) -> bool:
    """
    Rozpoznaje typy, których elementy mają dostać osobne pola TextBox.
    Identifies types whose elements should receive separate TextBoxes.
    """
    return isinstance(value, (list, tuple, set, frozenset))


def is_editor_value_editable(
    value: Any,
    active_objects: set[int] | None = None,
) -> bool:
    """
    Sprawdza, czy wartość można bezpiecznie odtworzyć z tekstu edytora.
    Checks whether a value can be safely reconstructed from editor text.
    """
    if value is None or isinstance(
        value,
        (bool, str, int, float, complex, bytes, bytearray),
    ):
        return True

    if isinstance(value, (Preferences, GenericPickleObject)):
        return False

    if active_objects is None:
        active_objects = set()

    object_id = id(value)

    # Cyklicznej struktury nie da się bezpiecznie odtworzyć przez
    # ast.literal_eval, dlatego pokazujemy ją później tylko w podglądzie.
    # A cyclic structure cannot be safely reconstructed with
    # ast.literal_eval, so it is shown as read-only in the future preview.
    if object_id in active_objects:
        return False

    active_objects.add(object_id)

    try:
        if isinstance(value, dict):
            return all(
                is_editor_value_editable(key, active_objects) and
                is_editor_value_editable(item, active_objects)
                for key, item in value.items()
            )

        if isinstance(value, (list, tuple, set, frozenset)):
            return all(
                is_editor_value_editable(item, active_objects)
                for item in value
            )

        return False
    finally:
        active_objects.remove(object_id)


def make_editor_value(value: Any) -> dict[str, Any]:
    return {
        "type": type_name(value),
        "text": editor_text(value),
    }


def make_variable(name: str, value: Any) -> dict[str, Any]:
    if is_editor_collection(value):
        editor_values = [make_editor_value(item) for item in value]
        is_collection = True
    else:
        editor_values = [make_editor_value(value)]
        is_collection = False

    return {
        "name": name,
        "type": type_name(value),
        "is_editable": is_editor_value_editable(value),
        "is_collection": is_collection,
        "values": editor_values,
    }


def persistent_variables(persistent_object: Any) -> list[dict[str, Any]]:
    if not hasattr(persistent_object, "__dict__"):
        raise TypeError(
            "The root persistent object does not have a __dict__."
        )

    variables = []

    for name, value in sorted(
        vars(persistent_object).items(),
        key=lambda item: str(item[0]),
    ):
        if name in DECODER_INTERNAL_FIELDS:
            continue

        variables.append(make_variable(str(name), value))

    return variables


def read_persistent(
    persistent_path: Path,
    *,
    debug_enabled: bool = False,
) -> tuple[Any, set[str]]:
    if not persistent_path.is_file():
        raise FileNotFoundError(
            f"File not found: {persistent_path.resolve()}"
        )

    compressed_data = persistent_path.read_bytes()

    try:
        pickle_data = zlib.decompress(compressed_data)
    except zlib.error as error:
        raise RuntimeError(
            "The persistent file could not be decompressed with zlib."
        ) from error

    unpickler = RenPyUnpickler(
        io.BytesIO(pickle_data),
        debug_enabled=debug_enabled,
    )

    return unpickler.load(), unpickler.unknown_classes


def create_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Reads a Ren'Py persistent file and outputs JSON for C#.",
    )
    parser.add_argument(
        "persistent_path",
        nargs="?",
        default="persistent",
        type=Path,
        help="Path to the persistent file (default: ./persistent).",
    )
    parser.add_argument(
        "--pretty",
        action="store_true",
        help="Formats the JSON for human-readable output.",
    )
    parser.add_argument(
        "--debug",
        action="store_true",
        help="Writes diagnostic information to stderr.",
    )
    return parser


def print_json(payload: dict[str, Any], *, pretty: bool) -> None:
    json.dump(
        payload,
        sys.stdout,
        ensure_ascii=False,
        indent=2 if pretty else None,
        separators=None if pretty else (",", ":"),
    )
    print()


def main() -> int:
    arguments = create_parser().parse_args()

    try:
        persistent_object, unknown_classes = read_persistent(
            arguments.persistent_path,
            debug_enabled=arguments.debug,
        )

        payload = {
            "success": True,
            "source": str(arguments.persistent_path.resolve()),
            "root_type": type_name(persistent_object),
            "unknown_classes": sorted(unknown_classes),
            "variables": persistent_variables(persistent_object),
        }
        print_json(payload, pretty=arguments.pretty)
        return 0

    except Exception as error:
        if arguments.debug:
            traceback.print_exc(file=sys.stderr)

        print_json(
            {
                "success": False,
                "error": {
                    "type": type(error).__name__,
                    "message": str(error),
                },
            },
            pretty=arguments.pretty,
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
