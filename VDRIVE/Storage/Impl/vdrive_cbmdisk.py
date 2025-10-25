#!/usr/bin/env python
import sys
import cbmdisk
import os
import traceback

def list_dir(disk_path):
    try:
        disk = cbmdisk.Disk(disk_path)
        print('0 "{}" 2a'.format(cbmdisk.to_ascii(disk.name)))
        for file in disk.files:
            name = cbmdisk.to_ascii(file.name).strip()
            print('{}  "{}"  {}'.format(file.size, name, file.type))
        print('{} BLOCKS FREE.'.format(disk.free_blocks))
    except Exception as e:
        print("Error listing directory:", e)
        traceback.print_exc()

def load_file(disk_path, filename):
    try:
        disk = cbmdisk.Disk(disk_path)
        target = disk.files.find(filename)
        if target:
            output_path = '{}.prg'.format(filename)
            target.save(output_path)
            print('Loaded:', os.path.abspath(output_path))
        else:
            print('File "{}" not found.'.format(filename))
    except Exception as e:
        print("Error loading file:", e)
        traceback.print_exc()

def save_file(disk_path, filepath):
    try:
        disk = cbmdisk.Disk(disk_path)
        index = len(disk.files)
        new_file = disk.files.create(index)
        new_file.name = os.path.basename(filepath).upper()[:16]
        new_file.type = "PRG"

        with open(filepath, "rb") as f:
            new_file.bytes = f.read()

        disk.save(disk_path)
        print('Saved: {} into {}'.format(filepath, disk_path))
    except Exception as e:
        print("Error saving file:", e)
        traceback.print_exc()

def main():
    try:
        if len(sys.argv) < 3:
            print('Usage: vdrive_cbmdisk.py [dir|load|save] disk.d64 [filename]')
            return

        command = sys.argv[1].lower()
        disk_path = sys.argv[2]

        if command == "dir":
            list_dir(disk_path)
        elif command == "load" and len(sys.argv) == 4:
            load_file(disk_path, sys.argv[3])
        elif command == "save" and len(sys.argv) == 4:
            save_file(disk_path, sys.argv[3])
        else:
            print('Invalid command or missing arguments.')
    except Exception as e:
        print("Unexpected error:", e)
        traceback.print_exc()

if __name__ == "__main__":
    main()
