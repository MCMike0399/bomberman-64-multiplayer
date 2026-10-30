"""Unapplied controller delta; deployed S3-v2 identity is preserved verbatim."""
import json

import builder_identity
import builder_policy_compact


def policy():
    value=builder_identity.policy(staging=True)
    statement=next(row for row in value['Statement']if row['Sid']=='ManageOwnedSchedule')
    statement['Action'].append('scheduler:UpdateSchedule')
    return value


def template():
    value=builder_identity.template(staging=True)
    permissions=policy();boundary=builder_policy_compact.compact(permissions)
    if len(json.dumps(boundary,separators=(',',':')))>6144:raise ValueError('Controller boundary exceeds IAM limit')
    value['Resources']['Boundary']['Properties']['PolicyDocument']=boundary
    value['Resources']['ControllerRole']['Properties']['Policies'][0]['PolicyDocument']=permissions
    return value
