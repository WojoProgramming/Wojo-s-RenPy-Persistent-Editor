from __future__ import annotations

import argparse
import ast
import io
import json
import os
import pickle
import pickletools
import sys
import tempfile
import traceback
import types
import zlib
from pathlib import Path
from typing import Any

from Decoding import (
    DECODER_INTERNAL_FIELDS,
    GenericPickleObject,
    Persistent,
    Preferences,
    RenPyUnpickler,
    RevertableDict,
    RevertableList,
    RevertableObject,
    RevertableSet,
    read_persistent,
    type_name,
)


class EncodingError(Exception):
    """
    Błąd danych wejściowych lub zapisu persistenta.
    An input-data or persistent-writing error.
    """


MISSING = object()


KNOWN_RENPY_CLASSES = {
    "Persistent": Persistent,
    "Preferences": Preferences,
    "RevertableObject": RevertableObject,
    "RevertableDict": RevertableDict,
    "RevertableList": RevertableList,
    "RevertableSet": RevertableSet,
}


DEFAULT_CLASS_MODULES = {
    "Persistent": "renpy.persistent",
    "Preferences": "renpy.preferences",
    "RevertableObject": "renpy.revertable",
    "RevertableDict": "renpy.revertable",
    "RevertableList": "renpy.revertable",
    "RevertableSet": "renpy.revertable",
}


def debug(message: str, enabled: bool) -> None:
    """
    Wypisuje diagnostykę do stderr, nie zanieczyszczając JSON-a.
    Writes diagnostics to stderr without contaminating the JSON output.
    """
    if enabled:
        print(message, file=sys.stderr)


def read_changes(changes_path: Path) -> list[dict[str, Any]]:
    """
    Wczytuje listę zmian przygotowaną przez aplikację C#.
    Reads the change list prepared by the C# application.
    """
    if not changes_path.is_file():
        raise FileNotFoundError(
            f"Changes file not found: {changes_path.resolve()}"
        )

    try:
        with changes_path.open("r", encoding="utf-8") as changes_file:
            payload = json.load(changes_file)
    except json.JSONDecodeError as error:
        raise EncodingError(
            f"The changes file is not valid JSON: {error}"
        ) from error

    if isinstance(payload, dict):
        changes = payload.get("changes")
    else:
        changes = payload

    if not isinstance(changes, list):
        raise EncodingError(
            "The changes JSON must be a list or an object containing "
            "a 'changes' list."
        )

    for index, change in enumerate(changes):
        if not isinstance(change, dict):
            raise EncodingError(
                f"Change at index {index} must be a JSON object."
            )

    return changes


def editor_value_text(editor_value: Any) -> tuple[str, str | None]:
    """
    Pobiera tekst oraz opcjonalny typ pojedynczego pola edytora.
    Gets the text and optional type of one editor field.
    """
    if isinstance(editor_value, str):
        return editor_value, None

    if not isinstance(editor_value, dict):
        raise EncodingError(
            "Every value must be a string or an object containing 'text'."
        )

    text = editor_value.get("text")
    declared_type = editor_value.get("type")

    if not isinstance(text, str):
        raise EncodingError("Every editor value must contain string 'text'.")

    if declared_type is not None and not isinstance(declared_type, str):
        raise EncodingError("The optional value 'type' must be a string.")

    return text, declared_type


def parse_bool(text: str) -> bool:
    normalized = text.strip().lower()

    if normalized == "true":
        return True

    if normalized == "false":
        return False

    raise EncodingError("A bool value must be 'True' or 'False'.")


def parse_none(text: str) -> None:
    if text.strip().lower() in ("none", "null"):
        return None

    raise EncodingError("A NoneType value must be 'None' or 'null'.")


def parse_literal(text: str, expected_description: str) -> Any:
    try:
        return ast.literal_eval(text)
    except (SyntaxError, ValueError) as error:
        raise EncodingError(
            f"The value is not a valid {expected_description}: {text!r}"
        ) from error


def merge_container(original: Any, parsed: Any) -> Any:
    """
    Zachowuje klasę istniejącego kontenera Ren'Py, zmieniając jego zawartość.
    Preserves an existing Ren'Py container class while changing its contents.
    """
    if isinstance(original, dict):
        if not isinstance(parsed, dict):
            raise EncodingError(
                f"Expected a dictionary, received {type_name(parsed)}."
            )

        merged_items = {}

        for key, value in parsed.items():
            if key in original:
                merged_items[key] = merge_value(original[key], value)
            else:
                merged_items[key] = value

        original.clear()
        original.update(merged_items)
        return original

    if isinstance(original, list):
        if not isinstance(parsed, list):
            raise EncodingError(
                f"Expected a list, received {type_name(parsed)}."
            )

        merged_items = []

        for index, value in enumerate(parsed):
            if index < len(original):
                merged_items.append(merge_value(original[index], value))
            else:
                merged_items.append(value)

        original.clear()
        original.extend(merged_items)
        return original

    if isinstance(original, tuple):
        if not isinstance(parsed, tuple):
            raise EncodingError(
                f"Expected a tuple, received {type_name(parsed)}."
            )

        return tuple(
            merge_value(original[index], value)
            if index < len(original)
            else value
            for index, value in enumerate(parsed)
        )

    if isinstance(original, set):
        if not isinstance(parsed, set):
            raise EncodingError(
                f"Expected a set, received {type_name(parsed)}."
            )

        original.clear()
        original.update(parsed)
        return original

    if isinstance(original, frozenset):
        if not isinstance(parsed, frozenset):
            raise EncodingError(
                f"Expected a frozenset, received {type_name(parsed)}."
            )

        return frozenset(parsed)

    return parsed


def merge_value(original: Any, parsed: Any) -> Any:
    if isinstance(original, (dict, list, tuple, set, frozenset)):
        return merge_container(original, parsed)

    return parsed


def parse_against_original(text: str, original: Any) -> Any:
    """
    Zamienia tekst z GUI na wartość tego samego rodzaju co oryginał.
    Converts GUI text into a value of the same kind as the original.
    """
    if original is None:
        return parse_none(text)

    if isinstance(original, bool):
        return parse_bool(text)

    if isinstance(original, str):
        return text

    if isinstance(original, int):
        try:
            return int(text.strip())
        except ValueError as error:
            raise EncodingError(f"Invalid int value: {text!r}") from error

    if isinstance(original, float):
        try:
            return float(text.strip())
        except ValueError as error:
            raise EncodingError(f"Invalid float value: {text!r}") from error

    if isinstance(original, complex):
        try:
            return complex(text.strip())
        except ValueError as error:
            raise EncodingError(f"Invalid complex value: {text!r}") from error

    if isinstance(original, bytes):
        parsed = parse_literal(text, "bytes literal")
        if not isinstance(parsed, bytes):
            raise EncodingError("The edited value must remain bytes.")
        return parsed

    if isinstance(original, bytearray):
        stripped = text.strip()

        if stripped.startswith("bytearray(") and stripped.endswith(")"):
            stripped = stripped[len("bytearray("):-1]

        parsed = parse_literal(stripped, "bytes or bytearray literal")
        if isinstance(parsed, bytes):
            return bytearray(parsed)
        if isinstance(parsed, bytearray):
            return parsed
        raise EncodingError("The edited value must remain a bytearray.")

    if isinstance(original, (dict, list, tuple, set)):
        parsed = parse_literal(text, type_name(original))
        return merge_container(original, parsed)

    if isinstance(original, frozenset):
        stripped = text.strip()

        if not stripped.startswith("frozenset(") or not stripped.endswith(")"):
            raise EncodingError(
                "A frozenset value must use the form frozenset({...})."
            )

        parsed_items = parse_literal(
            stripped[len("frozenset("):-1],
            "frozenset contents",
        )

        if not isinstance(parsed_items, (set, list, tuple)):
            raise EncodingError(
                "The contents of a frozenset must be a set, list, or tuple."
            )

        return frozenset(parsed_items)

    raise EncodingError(
        f"Editing values of type '{type_name(original)}' is not supported yet."
    )


def parse_from_declared_type(text: str, declared_type: str | None) -> Any:
    """
    Odtwarza nowy element kolekcji, gdy nie ma wartości wzorcowej.
    Reconstructs a new collection item when no original value is available.
    """
    if declared_type is None or declared_type == "str":
        return text

    if declared_type == "NoneType":
        return parse_none(text)

    if declared_type == "bool":
        return parse_bool(text)

    if declared_type == "int":
        try:
            return int(text.strip())
        except ValueError as error:
            raise EncodingError(f"Invalid int value: {text!r}") from error

    if declared_type == "float":
        try:
            return float(text.strip())
        except ValueError as error:
            raise EncodingError(f"Invalid float value: {text!r}") from error

    if declared_type == "complex":
        try:
            return complex(text.strip())
        except ValueError as error:
            raise EncodingError(f"Invalid complex value: {text!r}") from error

    if declared_type in ("bytes", "bytearray", "dict", "list", "tuple", "set"):
        parsed = parse_literal(text, declared_type)

        if declared_type == "bytearray" and isinstance(parsed, bytes):
            return bytearray(parsed)

        expected_types = {
            "bytes": bytes,
            "bytearray": bytearray,
            "dict": dict,
            "list": list,
            "tuple": tuple,
            "set": set,
        }

        if not isinstance(parsed, expected_types[declared_type]):
            raise EncodingError(
                f"The edited value must remain {declared_type}."
            )

        return parsed

    raise EncodingError(
        f"Adding values of type '{declared_type}' is not supported yet."
    )


def parse_editor_value(editor_value: Any, original: Any = MISSING) -> Any:
    text, declared_type = editor_value_text(editor_value)

    if original is not MISSING:
        return parse_against_original(text, original)

    return parse_from_declared_type(text, declared_type)


def apply_collection_change(original: Any, editor_values: list[Any]) -> Any:
    # Zbiory nie mają stabilnej kolejności między procesami Pythona, dlatego
    # ich elementy odtwarzamy z jawnych typów przekazanych przez dekoder.
    # Sets have no stable ordering between Python processes, so their items
    # are reconstructed from the explicit types supplied by the decoder.
    if isinstance(original, (set, frozenset)):
        parsed_values = [
            parse_editor_value(editor_value)
            for editor_value in editor_values
        ]

        if isinstance(original, set):
            original.clear()
            original.update(parsed_values)
            return original

        return frozenset(parsed_values)

    parsed_values = []
    original_values = list(original)

    for index, editor_value in enumerate(editor_values):
        if index < len(original_values):
            parsed_values.append(
                parse_editor_value(editor_value, original_values[index])
            )
        else:
            parsed_values.append(parse_editor_value(editor_value))

    if isinstance(original, list):
        original.clear()
        original.extend(parsed_values)
        return original

    if isinstance(original, tuple):
        return tuple(parsed_values)

    raise EncodingError(
        f"Type '{type_name(original)}' is not an editable collection."
    )


def apply_change(persistent_object: Any, change: dict[str, Any]) -> None:
    name = change.get("name")
    editor_values = change.get("values")

    if not isinstance(name, str) or not name:
        raise EncodingError("Every change must contain a non-empty 'name'.")

    if not isinstance(editor_values, list):
        raise EncodingError(
            f"Change '{name}' must contain a 'values' list."
        )

    variables = vars(persistent_object)

    if name not in variables or name in DECODER_INTERNAL_FIELDS:
        raise EncodingError(
            f"Variable '{name}' does not exist in the original persistent."
        )

    original = variables[name]

    if isinstance(original, (list, tuple, set, frozenset)):
        variables[name] = apply_collection_change(original, editor_values)
        return

    if len(editor_values) != 1:
        raise EncodingError(
            f"Scalar variable '{name}' must contain exactly one value."
        )

    variables[name] = parse_editor_value(editor_values[0], original)


def apply_changes(persistent_object: Any, changes: list[dict[str, Any]]) -> None:
    seen_names = set()

    for change in changes:
        name = change.get("name")

        if name in seen_names:
            raise EncodingError(f"Variable '{name}' was provided more than once.")

        seen_names.add(name)
        apply_change(persistent_object, change)


def detect_original_class_modules(pickle_data: bytes) -> dict[str, str]:
    """
    Odczytuje z pickle oryginalne moduły znanych klas Ren'Py.
    Reads the original modules of known Ren'Py classes from the pickle stream.
    """
    detected = {}

    recent_strings = []

    try:
        operations = pickletools.genops(pickle_data)

        for operation, argument, _position in operations:
            if operation.name in (
                "STRING",
                "UNICODE",
                "BINUNICODE",
                "SHORT_BINUNICODE",
                "BINUNICODE8",
            ) and isinstance(argument, str):
                recent_strings.append(argument)
                recent_strings = recent_strings[-2:]
                continue

            if operation.name == "GLOBAL" and isinstance(argument, str):
                try:
                    module, name = argument.split(" ", 1)
                except ValueError:
                    continue
            elif operation.name == "STACK_GLOBAL" and len(recent_strings) == 2:
                module, name = recent_strings
            else:
                continue

            if name in KNOWN_RENPY_CLASSES and name not in detected:
                detected[name] = module
    except (pickle.UnpicklingError, ValueError) as error:
        raise EncodingError(
            "The original pickle stream could not be inspected."
        ) from error

    return detected


def ensure_module(module_name: str) -> types.ModuleType:
    """
    Tworzy tymczasowy moduł wymagany przez standardowy Pickler.
    Creates a temporary module required by the standard Pickler.
    """
    parts = module_name.split(".")
    parent = None
    full_name = ""

    for part in parts:
        full_name = part if not full_name else f"{full_name}.{part}"

        module = sys.modules.get(full_name)
        if module is None:
            module = types.ModuleType(full_name)
            sys.modules[full_name] = module

        if parent is not None:
            setattr(parent, part, module)

        parent = module

    return parent


def clean_object_state(value: Any) -> dict[str, Any]:
    """
    Usuwa pola techniczne dekodera przed ponownym zapisem.
    Removes decoder-only fields before serializing again.
    """
    return {
        name: field_value
        for name, field_value in vars(value).items()
        if name not in DECODER_INTERNAL_FIELDS
    }


def prepare_known_classes_for_pickling(
    source_pickle: bytes,
) -> None:
    """
    Przywraca klasom zastępczym nazwy oczekiwane przez Ren'Py.
    Restores the names Ren'Py expects on the placeholder classes.
    """
    detected_modules = detect_original_class_modules(source_pickle)

    for name, class_object in KNOWN_RENPY_CLASSES.items():
        module_name = detected_modules.get(name, DEFAULT_CLASS_MODULES[name])
        module = ensure_module(module_name)

        class_object.__module__ = module_name
        class_object.__name__ = name
        class_object.__qualname__ = name
        class_object.__getstate__ = clean_object_state

        setattr(module, name, class_object)


def serialize_persistent(
    persistent_object: Any,
    source_pickle: bytes,
) -> bytes:
    prepare_known_classes_for_pickling(source_pickle)

    try:
        pickle_data = pickle.dumps(
            persistent_object,
            protocol=2,
            fix_imports=True,
        )
    except (pickle.PicklingError, AttributeError, TypeError) as error:
        raise EncodingError(
            f"The edited persistent could not be serialized: {error}"
        ) from error

    return zlib.compress(pickle_data)


def validate_encoded_persistent(encoded_data: bytes) -> None:
    """
    Sprawdza, czy gotowy plik można ponownie rozpakować i odczytać.
    Checks that the finished file can be decompressed and read again.
    """
    try:
        pickle_data = zlib.decompress(encoded_data)
        RenPyUnpickler(io.BytesIO(pickle_data)).load()
    except Exception as error:
        raise EncodingError(
            f"Validation of the encoded persistent failed: {error}"
        ) from error


def write_atomically(output_path: Path, data: bytes) -> None:
    """
    Zapisuje najpierw plik tymczasowy, aby nie zostawić połowy pliku.
    Writes a temporary file first to avoid leaving a partial output file.
    """
    output_path.parent.mkdir(parents=True, exist_ok=True)

    temporary_path = None

    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=output_path.parent,
            prefix=f".{output_path.name}.",
            suffix=".tmp",
            delete=False,
        ) as temporary_file:
            temporary_path = Path(temporary_file.name)
            temporary_file.write(data)

        os.replace(temporary_path, output_path)
    finally:
        if temporary_path is not None and temporary_path.exists():
            temporary_path.unlink()


def encode_persistent(
    source_path: Path,
    output_path: Path,
    changes_path: Path,
    *,
    debug_enabled: bool = False,
) -> int:
    if not source_path.is_file():
        raise FileNotFoundError(
            f"Persistent file not found: {source_path.resolve()}"
        )

    if source_path.resolve() == output_path.resolve():
        raise EncodingError(
            "The output path must be different from the original persistent path."
        )

    compressed_source = source_path.read_bytes()

    try:
        source_pickle = zlib.decompress(compressed_source)
    except zlib.error as error:
        raise EncodingError(
            "The original persistent could not be decompressed with zlib."
        ) from error

    persistent_object, unknown_classes = read_persistent(
        source_path,
        debug_enabled=debug_enabled,
    )

    if unknown_classes:
        class_list = ", ".join(sorted(unknown_classes))
        raise EncodingError(
            "This persistent contains classes that cannot be written safely yet: "
            f"{class_list}"
        )

    changes = read_changes(changes_path)
    debug(f"Applying {len(changes)} change(s).", debug_enabled)

    apply_changes(persistent_object, changes)

    encoded_data = serialize_persistent(persistent_object, source_pickle)
    validate_encoded_persistent(encoded_data)
    write_atomically(output_path, encoded_data)

    return len(changes)


def create_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Applies JSON changes to a Ren'Py persistent file and writes "
            "a new persistent file."
        ),
    )
    parser.add_argument(
        "source_path",
        type=Path,
        help="Path to the original persistent file.",
    )
    parser.add_argument(
        "output_path",
        type=Path,
        help="Path for the newly created persistent file.",
    )
    parser.add_argument(
        "changes_path",
        type=Path,
        help="Path to the UTF-8 JSON file containing edited variables.",
    )
    parser.add_argument(
        "--debug",
        action="store_true",
        help="Writes diagnostic information to stderr.",
    )
    return parser


def print_json(payload: dict[str, Any]) -> None:
    json.dump(
        payload,
        sys.stdout,
        ensure_ascii=False,
        separators=(",", ":"),
    )
    print()


def main() -> int:
    arguments = create_parser().parse_args()

    try:
        applied_changes = encode_persistent(
            arguments.source_path,
            arguments.output_path,
            arguments.changes_path,
            debug_enabled=arguments.debug,
        )

        print_json(
            {
                "success": True,
                "source": str(arguments.source_path.resolve()),
                "output": str(arguments.output_path.resolve()),
                "applied_changes": applied_changes,
            }
        )
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
            }
        )
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
