"""Проверка переводов интерфейса Shotly.

Сверяет src/Shotly/Localization/Languages/*.json с эталоном en.json и со списком строк в L.cs:
- в каждом языке те же ключи, что в en.json (нет пропущенных и лишних);
- подстановки {0}, {1}… те же, что в английском тексте;
- клавиши в подсказках отмечены звёздочками парно (*Esc*), и их столько же, сколько в английском;
- каждый язык из списка Loc.Languages есть файлом, и наоборот.

Запуск: python tools/check_translations.py. CI запускает его перед сборкой.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOC = ROOT / "src" / "Shotly" / "Localization"
LANGS = LOC / "Languages"

PLACEHOLDER = re.compile(r"\{(\d+)\}")


def load(path):
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        return None, [f"{path.name}: неверный JSON: {e}"]
    errors = [f"{path.name}: {k}: значение не строка" for k, v in data.items() if not isinstance(v, str)]
    return data, errors


def main():
    errors = []
    en, errs = load(LANGS / "en.json")
    errors += errs
    if en is None:
        print("\n".join(errors))
        return 1

    # Ключи в L.cs ↔ en.json.
    l_cs = (LOC / "L.cs").read_text(encoding="utf-8")
    props = set(re.findall(r"public static string (\w+)", l_cs))
    keys = set(en) - {"LanguageName"}
    for k in sorted(keys - props):
        errors.append(f"L.cs: нет свойства для ключа {k} из en.json")
    for k in sorted(props - keys):
        errors.append(f"en.json: нет ключа {k}, который есть в L.cs")
    for k in sorted(keys & props):
        count = len(set(PLACEHOLDER.findall(en[k])))
        m = re.search(rf"public static string {k}\(([^)]*)\)", l_cs)
        args = 0 if m is None else len([a for a in m.group(1).split(",") if a.strip()])
        if args != count:
            errors.append(f"L.cs: {k}: параметров {args}, а в en.json подстановок {count}")

    # Список языков в Loc.cs ↔ файлы.
    loc_cs = (LOC / "Loc.cs").read_text(encoding="utf-8")
    listed = re.findall(r'new\("([A-Za-z-]+)"', loc_cs)
    files = sorted(p.stem for p in LANGS.glob("*.json"))
    for code in sorted(set(listed) - set(files)):
        errors.append(f"Loc.Languages: язык {code} без файла {code}.json")
    for code in sorted(set(files) - set(listed)):
        errors.append(f"{code}.json: язык не добавлен в Loc.Languages")

    for path in sorted(LANGS.glob("*.json")):
        if path.stem == "en":
            continue
        data, errs = load(path)
        errors += errs
        if data is None:
            continue
        for k in sorted(set(en) - set(data)):
            errors.append(f"{path.name}: нет ключа {k}")
        for k in sorted(set(data) - set(en)):
            errors.append(f"{path.name}: лишний ключ {k}")
        for k in sorted(set(en) & set(data)):
            want, got = en[k], data[k]
            if not isinstance(got, str):
                continue
            if set(PLACEHOLDER.findall(want)) != set(PLACEHOLDER.findall(got)):
                errors.append(f"{path.name}: {k}: подстановки {sorted(set(PLACEHOLDER.findall(got)))}, "
                              f"нужны {sorted(set(PLACEHOLDER.findall(want)))}")
            if want.count("*") != got.count("*"):
                errors.append(f"{path.name}: {k}: звёздочек {got.count('*')}, в английском {want.count('*')}")
            if not got.strip():
                errors.append(f"{path.name}: {k}: пустой перевод")

    if errors:
        print("\n".join(errors))
        print(f"\nОшибок: {len(errors)}")
        return 1
    print(f"Переводы в порядке: {len(files)} языков, {len(keys)} строк.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
