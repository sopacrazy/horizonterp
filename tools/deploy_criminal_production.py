"""Deploy the additive six-organization/timed-production delta to HBRP only."""
import deploy_criminal_commerce as commerce

if __name__=='__main__':
    commerce.STAGE=commerce.ROOT/'tools/criminal-economy-validation/production-release'
    commerce.main(('018_criminal_production.sql',))
