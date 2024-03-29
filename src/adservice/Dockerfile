FROM --platform=amd64 adoptopenjdk/openjdk8:alpine as base

FROM base as builder

WORKDIR /app

COPY ["build.gradle", "gradlew", "./"]
COPY gradle gradle
RUN chmod +x gradlew
RUN ./gradlew downloadRepos

COPY . .
RUN chmod +x gradlew
RUN ./gradlew installDist

FROM base 

RUN apk --update add curl

RUN mkdir -p /opt/sfx
WORKDIR /opt/sfx

RUN curl -L https://github.com/signalfx/splunk-otel-java/releases/download/v1.26.0/splunk-otel-javaagent-all.jar -o splunk-otel-javaagent-all.jar

RUN GRPC_HEALTH_PROBE_VERSION=v0.4.1 && \
    wget -qO/bin/grpc_health_probe https://github.com/grpc-ecosystem/grpc-health-probe/releases/download/${GRPC_HEALTH_PROBE_VERSION}/grpc_health_probe-linux-amd64 && \
    chmod +x /bin/grpc_health_probe

WORKDIR /app
COPY --from=builder /app .

# Allow bottlenecks via copyright certification
#ENV ENABLE_COPYRIGHT_CERTIFICATION=true

EXPOSE 9555
ENTRYPOINT ["/app/build/install/AdService/bin/AdService"]
