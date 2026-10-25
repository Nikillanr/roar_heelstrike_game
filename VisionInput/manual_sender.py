"""Standard-library-only transport test, without camera/model dependencies."""
import argparse
from transport import StateSender
from hand_state import Observation


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7778)
    args = parser.parse_args()
    sender = StateSender(args.host, args.port, max_observation_age=None).start()
    print("Unity: open Vision_DesktopDemo and press Play.")
    print("lu / ld = left up / down; ru / rd = right up / down; then Enter.")
    print("u / d = both up / down; x = tracking loss; s = status; q = quit.")
    left = right = Observation("lowered", 1.0)
    sender.update_hands(left, right)
    try:
        while True:
            command = input("> ").strip().lower()
            if command == "q":
                break
            if command == "s":
                print(sender.status)
            elif command in ("u", "d", "x", "lu", "ld", "ru", "rd"):
                if command in ("lu", "ld"):
                    left = Observation("raised" if command == "lu" else "lowered", 1.0)
                elif command in ("ru", "rd"):
                    right = Observation("raised" if command == "ru" else "lowered", 1.0)
                else:
                    state = {"u": "raised", "d": "lowered", "x": "unknown"}[command]
                    left = right = Observation(state, 0.0 if state == "unknown" else 1.0)
                sender.update_hands(left, right)
                print(f"left: {left.state} | right: {right.state}")
    except (KeyboardInterrupt, EOFError):
        pass
    finally:
        sender.close()


if __name__ == "__main__":
    main()
