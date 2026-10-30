# Per-run Sancus builder staging

This is a proposed identity update for the SSM controller. It is not applied by
the generators or simulations. The default identity generator still reproduces
the reviewed, deployed identity without staging permissions. Use `--staging`
to generate the proposal, and review it before any CloudFormation update.

## Storage and guest access

Each owned run stack includes one bucket named
`sancus-builder-<date>-<source>-<nonce>-<account>`. It has the run and expiry tags,
all four Block Public Access settings enabled, BucketOwnerEnforced ownership,
SSE-S3/AES256 default encryption and a policy denying non-TLS requests. Objects
expire after one day; incomplete multipart uploads are also aborted after one
day. These lifecycle rules are crash safety, **not** proof of completed cleanup.

The controller may configure, transfer, enumerate and delete only the dated
`sancus-builder-20??????-*` bucket/object namespace. It also has cleanup rights
for object versions, delete markers and interrupted multipart uploads. The
existing single NVIDIA driver read remains separate. There are no new rights to
other buckets, EC2 instances, IAM identities or other Sancus services.

The guest role can GetObject from its exact bucket's `payload/` prefix and
PutObject into its exact `results/` prefix. It cannot overwrite frozen payloads,
list/configure/delete the bucket, or read another bucket. Object keys use neutral
names such as `payload/client.zip` and `results/windows.zip`.

GitHub API, runner-registration and Actions runtime tokens must never enter
SSM command text, parameters, bucket objects, guest environments or UserData.
The GitHub-hosted controller downloads the frozen CI artifacts locally first;
instances receive bytes only from their own bucket using the instance role.
SSM commands identify owned paths, object keys and expected hashes, not GitHub
download URLs or credentials. The transfer/controller implementation and its
credential-rejection tests must be completed before launch.

## Boundary size and review

AWS limits a managed permissions boundary to 6,144 non-whitespace characters.
The staging proposal preserves the existing inline statements and adds two S3
statements. Its boundary compacts repeated clauses only when the set of API-valid
action/resource/condition pairs remains identical. Unknown scoped actions stop
generation. It refuses combinations that would let SG launch authorization
authorize instance launch or turn a vendor read into a vendor write. S3 statements
remain verbatim because wildcard bucket ARNs can also match object paths.

The review packet must contain the readable inline diff, boundary diff, exact
template hash, size, permission delta and actual IAM simulations of controller,
boundary-only and guest policies. In particular, owned staging access must be
allowed, unrelated/foreign-owner access denied, and all prior protected-instance,
instance-size, IMDS, encryption and child-boundary denials retained. Validate the
identity, boundary and TLS-only resource policy with Access Analyzer.

```sh
python3 tools/ci/builder_identity.py --staging --output /tmp/builder-staging-identity.json
python3 tools/ci/builder_policy_simulation.py --staging \
  --protected-inventory /absolute/path/to/protected-instances.json \
  --output /absolute/path/to/new-review-directory
python3 -m unittest discover -s tools/ci -p 'test_builder*.py'
```

No command above creates a bucket, instance or identity. Only the explicitly
approved update to `sancus-builder-ci-identity` may change the standing role and
boundary; the external OIDC provider remains unmanaged.

## Cleanup contract

Before launch, discover every `sancus-builder-*` bucket, including buckets with
missing tags. Discovery is not deletion authority. Reconcile each against its
exact creation journal, account/region, run nonce and owned stack membership;
unknown ownership blocks launch and is reported rather than deleted by prefix.

Retain and hash the result bundle before teardown. Stop owned writers, enumerate
objects and versions/delete markers, abort and verify incomplete multipart
uploads, empty the exact owned bucket, then let owned stack deletion remove its
policy and bucket. Verify the bucket is absent after deletion; a submitted delete
or a one-day expiration is not success. AccessDenied and ownership mismatches are
not interpreted as absence. Cleanup must be repeatable after interruption, and
evidence failures cannot prevent independently authorized compute termination.
The final exact-ID/type audit and tag sweep must still cover every other created
resource. AWS-managed CloudTrail, SSM command history and billing records remain
listed as non-resource records.

See the [release policy and standing identity](sancus-builder-ci.md) for the
approval boundary, cost ledger and manual identity removal command.
