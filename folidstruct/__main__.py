"""Command line entry point.

    folidstruct                              open the template manager
    folidstruct --new [TEMPLATE] [--dir DIR] ask for a project name and create it
    folidstruct --new TEMPLATE --name NAME   create it without asking
    folidstruct --init                       write the default configuration
"""

import argparse
import sys
from pathlib import Path

from . import core


def main(argv=None):
    parser = argparse.ArgumentParser(
        prog=core.COMMAND, description="Create project folder structures from templates.")
    parser.add_argument("--new", metavar="TEMPLATE", nargs="?", const="",
                        help="create a project, optionally from TEMPLATE")
    parser.add_argument("--dir", default=".", help="where to create the project (default: .)")
    parser.add_argument("--name", help="project name; skips the dialog (needs TEMPLATE)")
    parser.add_argument("--init", action="store_true", help="write the default configuration")
    parser.add_argument("--version", action="version", version=f"{core.COMMAND} {core.VERSION}")
    args = parser.parse_args(argv)

    if args.init:
        try:
            core.ensure_config()
        except OSError as error:
            print(f"{core.COMMAND}: {error}", file=sys.stderr)
            return 1
        return 0

    if args.new is not None and args.name is not None:
        try:
            config = core.load_config()
            template = config.find_template(args.new)
            if template is None:
                raise core.AppError(f"template not found: {args.new!r}")
            use_date, use_counter = config.options_for(template)
            path = core.create_project(Path(args.dir), template, args.name,
                                       use_date, use_counter)
        except core.AppError as error:
            print(f"{core.COMMAND}: {error}", file=sys.stderr)
            return 1
        print(path)
        return 0

    if args.new is not None:
        from . import newproject
        return newproject.run(args.new or None, Path(args.dir).resolve())

    from . import manager
    return manager.run()


if __name__ == "__main__":
    sys.exit(main())
