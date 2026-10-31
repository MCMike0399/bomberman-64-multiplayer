"""Owner-bound low-level S3 calls and fail-closed staging creation checks."""
import json
import time
import builder_namespace as names
import builder_staging


class ApiError(RuntimeError):
    def __init__(self,code,message='S3 operation failed'):
        self.code=str(code);super().__init__(message+' ('+self.code+')')


class BucketCollision(RuntimeError):pass


# Polled objects that may not exist yet. Guests have no s3:ListBucket, so S3 answers
# a key that is not staged yet with 403 AccessDenied rather than 404 (runs 41178169234,
# 41198567742). The controller holds s3:ListBucket on its bucket, so for it a 403 is a
# real denial and fails at once. Throttling/timeouts are retried by both, and every
# poll is bounded by its caller's deadline. Mirrors builder_bootstrap.STAGED_PENDING.
MISSING=('NoSuchKey','404','NotFound')
TRANSIENT=('SlowDown','RequestTimeout','ServiceUnavailable','InternalError')
GUEST_PENDING=MISSING+('AccessDenied','403')+TRANSIENT
CONTROLLER_PENDING=MISSING+TRANSIENT+('UnverifiedError',)


def fetch_if_present(client,key,path,pending,samples,now=time.monotonic):
    """One step of a bounded poll: True once fetched, False (sampled) while pending; other errors raise."""
    try:client.call('get-object','--key',key,str(path))
    except ApiError as error:
        if error.code not in pending:raise
        if not samples or samples[-1]['code']!=error.code:samples.append({'code':error.code,'count':0,'firstAtMonotonic':now()})
        samples[-1]['count']+=1
        return False
    return True


class Objects:
    """Also used by guests: no GitHub credentials and no high-level s3 cp."""
    OPERATIONS={'head-bucket','get-bucket-tagging','get-bucket-location','get-public-access-block',
                'get-bucket-encryption','get-bucket-lifecycle-configuration','get-bucket-policy',
                'get-bucket-ownership-controls','get-bucket-versioning','get-object','put-object',
                'list-objects-v2','list-object-versions','list-multipart-uploads','list-parts',
                'abort-multipart-upload','delete-objects','delete-object','delete-bucket'}

    def __init__(self,aws,run):
        self.aws=aws;self.run=run;self.bucket=builder_staging.bucket_name(run)

    def call(self,operation,*arguments):
        if operation not in self.OPERATIONS:raise ValueError('Unsupported owner-bound S3 operation')
        if any(str(value).split('=',1)[0]in ('--bucket','--expected-bucket-owner','--cli-input-json','--cli-input-yaml',
                   '--profile','--region','--endpoint-url','--no-sign-request')for value in arguments):
            raise ValueError('Caller cannot override bucket identity or owner')
        return self.aws('s3api',operation,'--bucket',self.bucket,
                        '--expected-bucket-owner',names.ACCOUNT,*arguments)


def list_owned(aws):
    # ListBuckets has no ExpectedBucketOwner parameter; it lists the caller's
    # buckets. Verify STS first and consume every page explicitly.
    if aws('sts','get-caller-identity').get('Account')!=names.ACCOUNT:
        raise RuntimeError('Unexpected staging account')
    result=[];token=None;seen=set()
    while True:
        arguments=['--max-buckets','1000','--no-paginate']
        if token:arguments+=['--continuation-token',token]
        page=aws('s3api','list-buckets',*arguments)
        result.extend(page.get('Buckets',[]));token=page.get('ContinuationToken')
        if not token:return result
        if token in seen:raise RuntimeError('Incomplete/repeating account bucket inventory')
        seen.add(token)


def require_unused_name(aws,run):
    client=Objects(aws,run)
    if any(row['Name']==client.bucket for row in list_owned(aws)):
        raise BucketCollision('Never reuse a pre-existing staging bucket')
    try:
        client.call('head-bucket')
    except ApiError as error:
        if error.code not in ('404','NoSuchBucket','NotFound'):
            raise BucketCollision('Staging name is occupied or ownership is unverified')from error
    else:raise BucketCollision('Staging name already exists')
    return client


def provision_new(aws,run,create_owned_stack):
    client=require_unused_name(aws,run)
    # CreateBucket does not support ExpectedBucketOwner. The owned CFN stack
    # creates it under the verified account; no upload occurs until post-checks.
    try:create_owned_stack()
    except ApiError as error:
        if error.code in ('BucketAlreadyExists','BucketAlreadyOwnedByYou'):
            raise BucketCollision('Staging creation collided; abort without reuse')from error
        raise
    client.call('head-bucket')
    tags={row['Key']:row['Value']for row in client.call('get-bucket-tagging').get('TagSet',[])}
    if tags.get(names.RUN_TAG)!=run:raise RuntimeError('New staging bucket lacks the exact run ownership tag')
    return client


def verify_absent(aws,run):
    client=Objects(aws,run)
    if any(row['Name']==client.bucket for row in list_owned(aws)):return False
    try:client.call('head-bucket')
    except ApiError as error:
        # 403 alone is never absence. The complete account-owned bucket list
        # must also omit it; a globally reclaimed foreign name is not deleted.
        if error.code in ('404','NoSuchBucket','NotFound','403','AccessDenied'):return True
        raise
    return False


def purge_owned(client,authorize,stop_writers,deadline,now=time.monotonic):
    """Caller supplies the live journal/CFN ownership boundary, never a prefix sweep.

    Evidence retention happens in the controller before this call. Failed
    evidence does not veto independently authorized cleanup or writer shutdown.
    """
    def check():
        if now()>=deadline:raise TimeoutError('Owned bucket cleanup deadline reached')
        authorize()
    check();stop_writers()
    while True:
        check()
        page=client.call('list-multipart-uploads','--max-uploads','1000','--no-paginate')
        uploads=page.get('Uploads',[])
        if not uploads:
            if page.get('IsTruncated'):raise RuntimeError('Incomplete multipart inventory')
            break
        for upload in uploads:
            arguments=('--key',upload['Key'],'--upload-id',upload['UploadId'])
            for attempt in range(3):
                check()
                try:client.call('abort-multipart-upload',*arguments)
                except ApiError as error:
                    if error.code!='NoSuchUpload':raise
                try:parts=client.call('list-parts',*arguments).get('Parts',[])
                except ApiError as error:
                    if error.code!='NoSuchUpload':raise
                    parts=[]
                if not parts:break
            else:raise RuntimeError('Multipart upload still retains parts after abort')
    # Re-read the first page after each deletion; do not reuse a version marker
    # whose referenced object version has just been deleted.
    for operation,fields in [('list-object-versions',('Versions','DeleteMarkers')),
                             ('list-objects-v2',('Contents',))]:
        while True:
            check();page=client.call(operation,'--max-keys','1000','--no-paginate')
            objects=[]
            for field in fields:
                for row in page.get(field,[]):
                    item={'Key':row['Key']}
                    if 'VersionId'in row:item['VersionId']=row['VersionId']
                    objects.append(item)
            if not objects:
                if page.get('IsTruncated'):raise RuntimeError('Incomplete object inventory')
                break
            for start in range(0,len(objects),1000):
                check()
                result=client.call('delete-objects','--delete',json.dumps({'Objects':objects[start:start+1000],'Quiet':True}))
                if result.get('Errors'):raise RuntimeError('Owned staging object deletion failed')
    check();client.call('delete-bucket')
    if not verify_absent(client.aws,client.run):raise RuntimeError('Owned staging bucket remains after deletion')
    return {'bucket':client.bucket,'state':'absent'}
