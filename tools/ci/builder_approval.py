"""Disclose and bind the bounded paid plan before the environment approval."""
import datetime
import json
import math
from pathlib import Path
import re
import time

import builder_identity
import builder_namespace as names

ROOT = Path(__file__).resolve().parents[2]
RATE_CEILINGS = {'Linux': .526, 'Windows': .710}
MINIMUM_CAMPAIGN_SECONDS = 6000
CLEANUP_SECONDS = 1200


def require_unheld(value):
    if value != 'false':
        raise ValueError('Paid qualification hold is active; free audit/rehearsal remain available')


def estimate(rates):
    # Three hours of compute, encrypted roots, IPv4 and transfer/latency margin.
    return round(3 * sum(rates.values()) + 155 * .08 * 3 / 730 + 2 * .005 * 3 + 1.50, 2)


def ledger():
    return json.loads((ROOT / 'docs/sancus-builder-spend.json').read_text())


def build(source, verification_run, expiry, spending):
    if not re.fullmatch('[0-9a-f]{40}', source) or not str(verification_run).isdigit():
        raise ValueError('Approval requires an exact source and Verification run')
    deadline = datetime.datetime.fromisoformat(expiry)
    if deadline.tzinfo is None:
        raise ValueError('Approval expiry requires an explicit UTC offset')
    prior = spending['closedEstimateUsd']; budget = spending['budgetUsd']
    if any(type(value) not in (int, float) or not math.isfinite(value) for value in (prior, budget)) or not 0 <= prior <= budget <= 50:
        raise ValueError('Invalid cumulative budget')
    cost = estimate(RATE_CEILINGS)
    if cost > 10 or prior + cost > budget:
        raise ValueError('Paid approval exceeds the per-run or cumulative cap')
    return {'schemaVersion': 1, 'source': source, 'verificationRunId': str(verification_run),
            'account': names.ACCOUNT, 'region': names.REGION, 'fixedCampaignExpiryUtc': expiry,
            'instances': [{'os': osname, 'type': 'g4dn.xlarge', 'ami': ami, 'rootGb': size}
                          for osname, ami, size in [('Linux', builder_identity.AMIS[0], 75), ('Windows', builder_identity.AMIS[1], 80)]],
            'maximumLifetimeSeconds': 10800, 'minimumCampaignSeconds': MINIMUM_CAMPAIGN_SECONDS,
            'cleanupBudgetSeconds': CLEANUP_SECONDS, 'hourlyRateCeilingsUsd': dict(RATE_CEILINGS),
            'estimatedCampaignUsd': cost, 'priorEstimateUsd': prior, 'budgetUsd': budget,
            'remainingBudgetBeforeUsd': round(budget - prior, 2), 'remainingBudgetAfterUsd': round(budget - prior - cost, 2),
            'estimateScope': 'Conservative maximum reviewed hourly rates, three-hour compute/storage/IPv4 and transfer/termination allowance; not an invoice. Live prices above these ceilings refuse allocation.'}


def validate(approval, source, verification_run, expiry, spending, now=None):
    if approval != build(source, verification_run, expiry, spending):
        raise ValueError('Paid approval plan differs from source, Verification, instances or budget')
    remaining = datetime.datetime.fromisoformat(expiry).timestamp() - (time.time() if now is None else now)
    if remaining < MINIMUM_CAMPAIGN_SECONDS + CLEANUP_SECONDS:
        raise ValueError('Late approval: minimum campaign plus cleanup no longer fits; no allocation')
    if remaining > 10800:
        raise ValueError('Approval exceeds the maximum fixed lifetime')
    return approval


def validate_prepared(approval, plan, spending, now=None):
    validate(approval, plan['payloadSource'], plan['verificationRunId'], plan['fixedCampaignExpiryUtc'], spending, now)
    for key in ('account', 'region', 'instances', 'priorEstimateUsd', 'budgetUsd'):
        if plan.get(key) != approval[key]:
            raise ValueError('Prepared allocation differs from disclosed approval: ' + key)
    rates = plan.get('hourlyRatesUsd', {})
    if set(rates) != set(RATE_CEILINGS) or any(type(rates[k]) not in (int, float) or not math.isfinite(rates[k]) or not 0 < rates[k] <= RATE_CEILINGS[k] for k in rates):
        raise ValueError('Live price exceeds the pre-approval hourly ceiling')
    if plan.get('estimatedCampaignUsd') != estimate(rates) or plan['estimatedCampaignUsd'] > approval['estimatedCampaignUsd']:
        raise ValueError('Live estimate exceeds the disclosed approval')
    return approval


def summary(approval):
    lines = ['## PAID GPU campaign approval plan',
             f"Source: `{approval['source']}`. Verification: `{approval['verificationRunId']}`.",
             f"Account `{approval['account']}`, region `{approval['region']}`. Instance count: {len(approval['instances'])}."]
    for item in approval['instances']:
        lines.append(f"- {item['os']}: `{item['type']}`, AMI `{item['ami']}`, encrypted root {item['rootGb']} GiB.")
    lines += [f"Fixed expiry: `{approval['fixedCampaignExpiryUtc']}`; never extended after approval.",
              f"Conservative estimate: **${approval['estimatedCampaignUsd']:.2f}** (refuse above $10). Prior spending: ${approval['priorEstimateUsd']:.2f} / ${approval['budgetUsd']:.2f}.",
              f"Remaining before: ${approval['remainingBudgetBeforeUsd']:.2f}; after this reservation: ${approval['remainingBudgetAfterUsd']:.2f}.",
              'Approval needs at least 100 minutes for the campaign plus 20 minutes for cleanup remaining. Live prices and all bindings are checked again before creation.',
              approval['estimateScope']]
    return '\n'.join(lines) + '\n'
