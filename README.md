# Compose Test Environment:
Setup integration test environment via docker compose.
This library simplify run and debug tests from IDE: just run, this library setup environment automatically and tear down after test execution
Also it support run tests during CI as part of docker compose service.
So your tests will work inside docker-compose (as part of CI process) and outside of docker-compose (as standard IDE workflow).

## Installation

1. Add reference
    ```
    dotnet add ComposeTestEnvironment.xUnit
    ```

2. Create docker file with dotnet sdk and copy to it all project.

   See [dockerfile.tests](dockerfile.tests) for example.

3. Create docker-compose yaml file.

   Specify necessary environment services, don't expose internal ports.

   Specify service based on image in step 2, with command `dotnet test`, with special environment variable `UNDER_COMPOSE` (see [testcompose.yml](testcompose.yml)).

4. Add environment descriptor to you test assembly

   Create class `ComposeDescriptor` with default constructor inherited from `DockerComposeDescriptor`.

   Specify docker compose file name (this library scans all parent directories for the file with the same name).

   Specify default ports for services specified by you docker-compose file.

5. Use `IClassFixture<DockerComposeEnvironmentFixture<ComposeDescriptor>>`.

   Or you can create class inherited from `DockerComposeEnvironmentFixture<ComposeDescriptor>`.

   You can inject into initialization pipeline your code.

6. Setup your CI system to run tests

To run tests:
```
docker-compose --file testcompose.yml up --abort-on-container-exit --build
docker-compose --file testcompose.yml kill
docker-compose --file testcompose.yml down --rmi local
```

## Motivation
Integration tests (or end-to-end) usually need to setup some environment. During the era of docker I don't want
to install any of software on my desktop or have additional shared service with all environment installed. So I
put all necessary environment services in docker-compose file.

Also I adept of zero configuration approach. I.e. after `git clone` you should be able to run `dotnet test` and on
the main branch you should have all tests passed without additional setup and configuration. Also I would like to
run/debug my tests directly from my favorite IDE.

The simplest answer - create docker-compose file and start environment before tests doesn't work:
- you should expose ports from containers, so you should have free ports, you cannot run tests on different branches in parallel
- you should support different description of necessary environment for local test run and for CI test run
- you should detect mode of test run to change connection string, etc.

This test addon tries to fix these issues:
- you specify all necessary environment in single docker-compose file
- in this docker-compose file you also run tests as additional service (via `dotnet test` command)
- on local test run (or run/debug tests from IDE):
   - it finds free ports on localhost
   - generates new docker-compose file with exposed ports
   - provides ability to «service discovery» (i.e. you should use different hosts/ports to connect to the services
     depending on type of run)
   - stops previously run container
   - starts docker compose file before first test used
   - wait for service starts (based on ports and/or specific message in service output or you can provide your own logic for this)
   - tear down docker compose before test stop (if necessary, you can leave container running)
   - on any docker related failure print docker output to test output
   - print docker output as xunit diagnostic message
- it doesnt use test collection to setup and teardown environment (as all tests in collections executed without parallelism)

## Sample

See:
- [testcompose.yml](testcompose.yml) - docker-compose environment with service to run test
- [ComposeDescriptor.cs](Sample/ComposeDescriptor.cs) - environment descriptor in code.
- [SampleComposeFixture.cs](Sample/SampleComposeFixture.cs) - xUnit class fixture, you should mark your tests
  via `IClassFixture<SampleComposeFixture>` to work with environment services
- [SqlTest.cs](Sample/SqlTest.cs) - example of test

## FAQ
1. How to display docker compose logs
Create xunit.runner.json:
```json
{
    "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
    "diagnosticMessages": true
}
```
Put into your test cspoj:
```msbuild
   <ItemGroup>
        <Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>
```
Now
```shell
dotnet test
```
will display docker container logs

2. How to don't clean containers after test finish

Override `DockerComposeDescriptor.DownOnComplete` property to return false.

## Working modes:

1. Run tests inside docker compose container
   Provide discovery mechanism (service name as host).
   Delay test start until service respond on specified tcp port

2. Run tests from IDE
   Find free ports, bind to them docker compose services
   Discovery uses localhost with exposed ports
   Delay test start until all services respond on tcp port
   Compose pull before test run
   Compose down before test run (to stop compose after terminate debug)
   Compose down after test run

3. (Not yet implemented) Run tests from IDE, attach to existing containers, start new if necessary
   Find existing running compose
   If good - use it, if not start it as in 2. But do not tear down it after test execution finished (to reuse it on next time)
