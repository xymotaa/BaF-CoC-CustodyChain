#!/bin/bash
# Define as variáveis de ambiente do peer CLI para esta rede local
# (adaptado do setOrgEnv.sh do fabric-samples para a estrutura fabric/network
# deste projeto, onde não há uma subpasta test-network).
#
# Uso: source setOrgEnvCustom.sh <1|2>

ORG=${1:-1}
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

export FABRIC_CFG_PATH="${DIR}/../config"
export CORE_PEER_TLS_ENABLED=true
export ORDERER_CA="${DIR}/organizations/ordererOrganizations/example.com/tlsca/tlsca.example.com-cert.pem"
export PEER0_ORG1_CA="${DIR}/organizations/peerOrganizations/org1.example.com/tlsca/tlsca.org1.example.com-cert.pem"
export PEER0_ORG2_CA="${DIR}/organizations/peerOrganizations/org2.example.com/tlsca/tlsca.org2.example.com-cert.pem"

if [ "$ORG" = "1" ]; then
    export CORE_PEER_LOCALMSPID="Org1MSP"
    export CORE_PEER_MSPCONFIGPATH="${DIR}/organizations/peerOrganizations/org1.example.com/users/Admin@org1.example.com/msp"
    export CORE_PEER_ADDRESS="localhost:7051"
    export CORE_PEER_TLS_ROOTCERT_FILE="${PEER0_ORG1_CA}"
elif [ "$ORG" = "2" ]; then
    export CORE_PEER_LOCALMSPID="Org2MSP"
    export CORE_PEER_MSPCONFIGPATH="${DIR}/organizations/peerOrganizations/org2.example.com/users/Admin@org2.example.com/msp"
    export CORE_PEER_ADDRESS="localhost:9051"
    export CORE_PEER_TLS_ROOTCERT_FILE="${PEER0_ORG2_CA}"
else
    echo "Organização inválida: $ORG (use 1 ou 2)"
    return 1 2>/dev/null || exit 1
fi
