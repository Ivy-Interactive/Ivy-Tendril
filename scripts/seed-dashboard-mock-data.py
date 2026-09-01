#!/usr/bin/env python3
"""Seed a TENDRIL_HOME with mock plans so the dashboard shows realistic data.

Generates plan folders (plan.yaml + costs.csv + Revisions/001.md) spread over
the past months, including mocked git activity in the form of merged PR links
on completed plans. Tendril syncs the folders into its SQLite database on the
next startup; the script deletes the existing tendril.db so a clean re-sync
happens automatically.

Typical usage against a scratch home (never point it at a home you care about,
existing plans and the database in the target home are replaced):

    python3 scripts/seed-dashboard-mock-data.py --home /tmp/tendril-home

Then run Tendril with TENDRIL_HOME=/tmp/tendril-home and open the Dashboard.
"""

import argparse
import csv
import math
import random
import shutil
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

VERBS = [
    "Add", "Fix", "Refactor", "Improve", "Migrate", "Remove", "Optimize",
    "Document", "Redesign", "Harden", "Simplify", "Extend", "Cache", "Localize",
]
COMPONENTS = [
    "webhook dispatcher", "billing exports", "vault sync", "session store",
    "search indexing", "release pipeline", "PR review flow", "audit logging",
    "settings panel", "notification service", "rate limiting", "dark mode",
    "onboarding wizard", "database migrations", "file uploads", "API pagination",
    "error reporting", "keyboard shortcuts", "dashboard charts", "team invites",
]
LEVELS = ["Feature", "Feature", "Feature", "Bug", "Bug", "Chore", "Nitpick"]
PROMPTWARES = ["CreatePlan", "ExecutePlan", "CreatePr", "ReviewFix"]


def slugify(title: str) -> str:
    return "".join(c if c.isalnum() else "-" for c in title.lower()).strip("-")[:40]


def iso(dt: datetime) -> str:
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


def weekday_biased_day(rng: random.Random, year: int, month: int, max_day: int) -> int:
    while True:
        day = rng.randint(1, max_day)
        weekday = datetime(year, month, day).weekday()
        if weekday < 5 or rng.random() < 0.25:
            return day


def month_start(anchor: datetime, months_back: int) -> datetime:
    year = anchor.year
    month = anchor.month - months_back
    while month <= 0:
        month += 12
        year -= 1
    return datetime(year, month, 1)


class Seeder:
    def __init__(self, args: argparse.Namespace):
        self.rng = random.Random(args.seed)
        self.home = Path(args.home).expanduser()
        self.project = args.project
        self.repo = args.repo
        self.months = args.months
        self.plan_index = 0
        self.pr_number = 100
        self.now = datetime.now(timezone.utc).replace(tzinfo=None)

    def next_pr_links(self, count: int) -> list[str]:
        links = []
        for _ in range(count):
            self.pr_number += self.rng.randint(1, 9)
            links.append(f"https://github.com/mock/{slugify(self.project)}/pull/{self.pr_number}")
        return links

    def write_plan(self, state: str, created: datetime, updated: datetime,
                   prs: list[str], cost_scale: float) -> None:
        self.plan_index += 1
        title = f"{self.rng.choice(VERBS)} {self.rng.choice(COMPONENTS)}"
        folder = self.home / "Plans" / f"{self.plan_index:05d}-{slugify(title)}"
        folder.mkdir(parents=True)

        prs_yaml = "prs: []" if not prs else "prs:\n" + "\n".join(f"  - {p}" for p in prs)
        plan_yaml = f"""schemaVersion: 3
state: {state}
project: {self.project}
level: {self.rng.choice(LEVELS)}
title: {title}
repos:
  - {self.repo}
created: {iso(created)}
updated: {iso(updated)}
{prs_yaml}
commits: []
verifications: []
relatedPlans: []
dependsOn: []
"""
        (folder / "plan.yaml").write_text(plan_yaml)

        revisions = folder / "Revisions"
        revisions.mkdir()
        (revisions / "001.md").write_text(f"# {title}\n\nMock plan seeded for dashboard previews.\n")

        if state != "Draft":
            rows = self.rng.randint(2, 4)
            with open(folder / "costs.csv", "w", newline="") as f:
                writer = csv.writer(f)
                writer.writerow(["Promptware", "Tokens", "Cost"])
                for i in range(rows):
                    cost = round(self.rng.uniform(0.4, 2.6) * cost_scale, 4)
                    tokens = int(cost * self.rng.uniform(85_000, 115_000))
                    writer.writerow([PROMPTWARES[i % len(PROMPTWARES)], tokens, cost])

    def seed_history(self) -> None:
        earliest_recent = self.now - timedelta(days=7)
        for back in range(self.months - 1, 0, -1):
            start = month_start(self.now, back)
            ramp = (self.months - 1 - back) / max(1, self.months - 1)
            volume = (4 + 24 * ramp) * (1 + 0.25 * math.sin(back * 0.9))
            volume = max(2, int(volume * self.rng.uniform(0.8, 1.2)))
            cost_scale = max(0.4, (0.6 + 1.6 * ramp) * (1 + 0.3 * math.sin(back * 1.3)))
            days_in_month = ((month_start(self.now, back - 1) - timedelta(days=1)).day)

            max_day = days_in_month
            if (start.year, start.month) == (earliest_recent.year, earliest_recent.month):
                max_day = earliest_recent.day - 1
                if max_day < 1:
                    continue
                volume = max(1, int(volume * max_day / days_in_month))

            for _ in range(volume):
                day = weekday_biased_day(self.rng, start.year, start.month, max_day)
                created = start.replace(day=day, hour=self.rng.randint(6, 18),
                                        minute=self.rng.randint(0, 59))
                updated = created + timedelta(hours=self.rng.uniform(1, 30))
                state = "Completed" if self.rng.random() < 0.87 else "Failed"
                prs = self.next_pr_links(self.rng.randint(1, 3)) if state == "Completed" else []
                self.write_plan(state, created, updated, prs, cost_scale)

        current_start = month_start(self.now, 0)
        if earliest_recent > current_start:
            days_available = max(1, (earliest_recent - current_start).days)
            volume = max(1, int(28 * days_available / 30 * self.rng.uniform(0.8, 1.2)))
            for _ in range(volume):
                day = weekday_biased_day(self.rng, current_start.year, current_start.month, days_available)
                created = current_start.replace(day=day, hour=self.rng.randint(6, 18),
                                               minute=self.rng.randint(0, 59))
                updated = created + timedelta(hours=self.rng.uniform(1, 30))
                state = "Completed" if self.rng.random() < 0.87 else "Failed"
                prs = self.next_pr_links(self.rng.randint(1, 3)) if state == "Completed" else []
                self.write_plan(state, created, updated, prs, 2.2)

    def recent_time(self, day: datetime, days_ago: int, latest_hour: int) -> datetime:
        hour_cap = max(0, min(latest_hour, self.now.hour - 1)) if days_ago == 0 else latest_hour
        return day.replace(hour=self.rng.randint(0, hour_cap), minute=self.rng.randint(0, 59))

    def clamp(self, moment: datetime) -> datetime:
        return min(moment, self.now)

    def seed_recent(self) -> None:
        for days_ago in range(6, -1, -1):
            day = self.now - timedelta(days=days_ago)
            for _ in range(self.rng.randint(2, 5)):
                created = self.recent_time(day, days_ago, 16)
                updated = self.clamp(created + timedelta(hours=self.rng.uniform(1, 6)))
                self.write_plan("Completed", created, updated,
                                self.next_pr_links(self.rng.randint(1, 3)), 2.2)
            if self.rng.random() < 0.4:
                created = self.recent_time(day, days_ago, 16)
                self.write_plan("Failed", created, self.clamp(created + timedelta(hours=2)), [], 1.5)
            if days_ago <= 4 and self.rng.random() < 0.8:
                created = self.recent_time(day, days_ago, 18)
                self.write_plan("Review", created, self.clamp(created + timedelta(hours=3)),
                                self.next_pr_links(1), 2.0)
            if days_ago <= 2:
                for _ in range(self.rng.randint(1, 3)):
                    created = self.recent_time(day, days_ago, 20)
                    self.write_plan("Draft", created, created, [], 0)

    def run(self) -> None:
        plans_dir = self.home / "Plans"
        if plans_dir.exists():
            shutil.rmtree(plans_dir)
        plans_dir.mkdir(parents=True)

        self.seed_history()
        self.seed_recent()

        (plans_dir / ".counter").write_text(str(self.plan_index))
        for db_file in ["tendril.db", "tendril.db-shm", "tendril.db-wal"]:
            (self.home / db_file).unlink(missing_ok=True)

        print(f"Seeded {self.plan_index} plans into {plans_dir}")
        print("Deleted tendril.db so the next Tendril start performs a clean re-sync.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--home", required=True,
                        help="TENDRIL_HOME to seed (its Plans folder is replaced)")
    parser.add_argument("--project", default="Test", help="project name used on the plans")
    parser.add_argument("--repo", default="/tmp/mock-repo", help="repo path written to the plans")
    parser.add_argument("--months", type=int, default=24, help="months of history to generate")
    parser.add_argument("--seed", type=int, default=7, help="random seed")
    parser.add_argument("--force", action="store_true",
                        help="allow seeding into ~/.tendril (destructive)")
    args = parser.parse_args()

    home = Path(args.home).expanduser().resolve()
    real_home = (Path.home() / ".tendril").resolve()
    if home == real_home and not args.force:
        sys.exit("Refusing to overwrite ~/.tendril without --force")

    Seeder(args).run()


if __name__ == "__main__":
    main()
