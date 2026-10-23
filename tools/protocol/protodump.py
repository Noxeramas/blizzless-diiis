"""Extract embedded protobuf FileDescriptorProtos from a binary and print them as .proto-like text.

Usage: python -I protodump.py <binary> <substring-of-proto-filename> [...]
"""
import sys
from google.protobuf import descriptor_pb2

FILE_FIELDS = set(range(1, 15)) | {999}
LABELS = {1: "optional", 2: "required", 3: "repeated"}
TYPES = {1: "double", 2: "float", 3: "int64", 4: "uint64", 5: "int32", 6: "fixed64", 7: "fixed32", 8: "bool",
         9: "string", 12: "bytes", 13: "uint32", 15: "sfixed32", 16: "sfixed64", 17: "sint32", 18: "sint64"}


def varint(data, pos):
    result = shift = 0
    while True:
        b = data[pos]
        pos += 1
        result |= (b & 0x7F) << shift
        if not b & 0x80:
            return result, pos
        shift += 7
        if shift > 63:
            raise ValueError("varint too long")


def descriptor_length(data, start):
    """Walk top-level fields until one is not a plausible FileDescriptorProto field."""
    pos = start
    while pos < len(data):
        try:
            key, after = varint(data, pos)
        except (IndexError, ValueError):
            break
        field, wire = key >> 3, key & 7
        if field not in FILE_FIELDS:
            break
        if wire == 2:
            length, after = varint(data, after)
            after += length
        elif wire == 0:
            _, after = varint(data, after)
        else:
            break
        if after > len(data):
            break
        pos = after
    return pos - start


def show_message(msg, indent):
    pad = "  " * indent
    print(f"{pad}message {msg.name} {{")
    for nested in msg.nested_type:
        show_message(nested, indent + 1)
    for enum in msg.enum_type:
        show_enum(enum, indent + 1)
    for f in msg.field:
        ftype = f.type_name or TYPES.get(f.type, f"type{f.type}")
        print(f"{pad}  {LABELS.get(f.label, '')} {ftype} {f.name} = {f.number};")
    print(f"{pad}}}")


def show_enum(enum, indent):
    pad = "  " * indent
    values = ", ".join(f"{v.name}={v.number}" for v in enum.value)
    print(f"{pad}enum {enum.name} {{ {values} }}")


def main():
    data = open(sys.argv[1], "rb").read()
    for wanted in sys.argv[2:]:
        idx = data.find(wanted.encode())
        while idx != -1:
            # Serialized FileDescriptorProto starts with 0x0A <len> <name>; back up to the start of the name.
            name_start = data.rfind(b"\n", max(0, idx - 200), idx + 1)
            start = name_start
            name_len, name_pos = varint(data, start + 1)
            name = data[name_pos:name_pos + name_len]
            if wanted.encode() in name and name.endswith(b".proto"):
                fdp = descriptor_pb2.FileDescriptorProto()
                fdp.ParseFromString(data[start:start + descriptor_length(data, start)])
                print(f"// file {fdp.name}  package {fdp.package}")
                for dep in fdp.dependency:
                    print(f"import \"{dep}\";")
                for enum in fdp.enum_type:
                    show_enum(enum, 0)
                for msg in fdp.message_type:
                    show_message(msg, 0)
                for svc in fdp.service:
                    print(f"service {svc.name} {{  // raw options: {svc.options.SerializeToString()!r}")
                    for m in svc.method:
                        print(f"  rpc {m.name}({m.input_type}) returns ({m.output_type});  // raw options: {m.options.SerializeToString().hex()}")
                    print("}")
                print()
                break
            idx = data.find(wanted.encode(), idx + 1)


main()
