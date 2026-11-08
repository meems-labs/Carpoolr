"""Promote both successfully built images to one immutable Git revision."""

import re
import sys
from pathlib import Path


def main() -> None:
    if len(sys.argv) != 2 or re.fullmatch(r"[0-9a-f]{40}", sys.argv[1]) is None:
        raise SystemExit("Supply the full, lowercase Git SHA of the tested image build.")

    path = (
        Path(__file__).resolve().parents[1]
        / "deploy/kubernetes/overlays/staging/kustomization.yaml"
    )
    source = path.read_text(encoding="utf-8")
    for image in ("carpoolr-api", "carpoolr-web"):
        pattern = rf"(- name: ghcr\.io/meems-labs/{image}\s+newTag: )[^\s]+"
        source, count = re.subn(pattern, lambda match: match[1] + sys.argv[1], source)
        if count != 1:
            raise SystemExit(f"Expected exactly one staging image declaration for {image}.")

    path.write_text(source, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
