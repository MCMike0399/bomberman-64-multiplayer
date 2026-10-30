"""Conservative elapsed-resource estimate, explicitly separate from AWS invoices."""
import datetime


def epoch(value):return datetime.datetime.fromisoformat(value).timestamp()


def calculate(plan,rows,resources,observed_at,evidence_bytes=0):
    rates=plan.get('hourlyRatesUsd',{});owned=[row for row in rows if row['event']=='CREATED'and row['type']=='AWS::EC2::Instance']
    if owned and set(rates)!={'Linux','Windows'}:
        return {'complete':False,'reason':'Actual preparation-time hourly rates are missing','invoice':False}
    states={(row['type'],row['id']):row['finalState']for row in resources}
    initial=[row['timestampUtc']for row in rows if row['event']=='INTENT'and row['type']=='AWS::CloudFormation::Stack']
    compute=0;lifetimes=[]
    for instance in owned:
        identity=instance['id'];logical=instance['details'].get('logicalId','')
        system='Linux'if logical=='LinuxInstance'else'Windows'if logical=='WindowsInstance'else None
        if system is None or states.get(('AWS::EC2::Instance',identity))not in ('absent','terminated'):
            return {'complete':False,'reason':'Instance identity/final state is unverified','invoice':False}
        observations=[row['details']for row in rows if row['event']=='STATUS'and row['id']==identity]
        launches=[item['launchTime']for item in observations if item.get('launchTime')]
        endings=[item['terminatedObservedAtUtc']for item in observations if item.get('terminatedObservedAtUtc')]
        starts=launches or initial
        if not starts:return {'complete':False,'reason':'No durable lifetime start observation','invoice':False}
        start=min(starts,key=epoch);end=min(endings,key=epoch)if endings else observed_at
        seconds=max(60,epoch(end)-epoch(start));amount=seconds/3600*rates[system];compute+=amount
        lifetimes.append({'id':identity,'os':system,'startedAtUtc':start,'terminatedObservedAtUtc':end,
                          'secondsUpperBound':round(seconds,3),'hourlyUsd':rates[system],'computeUsd':round(amount,6),
                          'startBasis':'AWS launch time'if launches else'Conservative creation-intent time',
                          'endBasis':'First terminated observation'if endings else'Conservative final audit observation'})
    # Covers storage, public IPv4, requests and latency. Egress is based on the
    # actually retained byte count, conservatively including duplicate checkpoints.
    allowance=.25 if owned else 0
    transfer=evidence_bytes/(1024**3)*.09
    estimate=round(compute+allowance+transfer,2)
    return {'complete':True,'invoice':False,'currency':'USD','estimateUsd':estimate,
            'computeUsd':round(compute,6),'storageIpv4RequestsAllowanceUsd':allowance,
            'transferEstimateUsd':round(transfer,6),'retainedBytes':evidence_bytes,'instances':lifetimes,
            'priorClosedEstimateUsd':plan.get('priorEstimateUsd'),
            'cumulativeEstimateUsd':round(plan.get('priorEstimateUsd',0)+estimate,2),
            'scope':'Conservative observed elapsed-resource estimate; actual AWS billing remains authoritative.'}
